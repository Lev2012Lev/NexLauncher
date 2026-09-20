using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Installers;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionLoader;
using NexLauncher.Models;

namespace NexLauncher.Services;

/// <summary>Installs vanilla releases from Mojang into independent instance directories.</summary>
public sealed class MinecraftService : IMinecraftService
{
    private const string MarkerName = ".nexlauncher-installed";
    private readonly string dataDirectory;
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public MinecraftService(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        this.dataDirectory = Path.GetFullPath(dataDirectory);
    }

    public async Task<IReadOnlyList<MinecraftRelease>> GetVersionsAsync(CancellationToken cancellationToken)
    {
        // This directory only caches the official catalogue. It is not an instance.
        var launcher = CreateLauncher(Path.Combine(dataDirectory, "catalogue"));
        var versions = await launcher.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);
        return versions
            .Where(version => version.Type is "release" or "snapshot")
            .OrderByDescending(version => version.ReleaseTime)
            .Select(version => new MinecraftRelease(version.Name, version.Type!, version.ReleaseTime))
            .ToArray();
    }

    public bool IsInstalled(GameInstance instance)
    {
        try
        {
            var path = GetGamePath(instance);
            ValidateVersionId(instance.VersionId);
            var marker = Path.Combine(path, MarkerName);
            var versionDirectory = Path.Combine(path, "versions", instance.VersionId);
            return File.Exists(marker)
                && string.Equals(File.ReadAllText(marker), instance.VersionId, StringComparison.Ordinal)
                && IsNonEmptyFile(Path.Combine(versionDirectory, instance.VersionId + ".json"))
                && IsNonEmptyFile(Path.Combine(versionDirectory, instance.VersionId + ".jar"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public async Task InstallAsync(GameInstance instance, IProgress<LaunchProgress> progress,
        CancellationToken cancellationToken)
    {
        var settings = SnapshotAndValidate(instance);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var launcher = CreateLauncher(GetGamePath(settings), settings.JavaPath);
            await InstallCoreAsync(launcher, settings.VersionId, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async Task<int> LaunchAsync(GameInstance instance, MSession session,
        IProgress<LaunchProgress> progress, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(log);
        cancellationToken.ThrowIfCancellationRequested();
        var settings = SnapshotAndValidate(instance);
        var authorizedSession = CopyAuthorizedSession(session);
        await operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var launcher = CreateLauncher(GetGamePath(settings), settings.JavaPath);
            // Verify hashes and recover missing files on every launch, even when a marker exists.
            await InstallCoreAsync(launcher, settings.VersionId, progress, cancellationToken).ConfigureAwait(false);
            progress.Report(new LaunchProgress("Подготовка запуска…"));
            using var process = await launcher.BuildProcessAsync(settings.VersionId, new MLaunchOption
            {
                Session = authorizedSession,
                MaximumRamMb = settings.MemoryMb,
                JavaPath = string.IsNullOrWhiteSpace(settings.JavaPath) ? null : settings.JavaPath,
                GameLauncherName = "NexLauncher",
                GameLauncherVersion = "0.1"
            }, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(process.StartInfo.FileName))
                throw new InvalidOperationException("Подходящая Java не найдена. Укажите путь к Java в настройках сборки.");

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start())
                throw new InvalidOperationException("Не удалось запустить Minecraft.");

            progress.Report(new LaunchProgress("Игра запущена", null, true));
            // Once started the game owns its lifetime. Cancellation/closing the launcher must not kill it.
            // Drain both pipes concurrently so a full stderr buffer cannot block the Java process.
            var logGate = new object();
            var stdout = ReadLogAsync(process.StandardOutput, log, logGate, authorizedSession);
            var stderr = ReadLogAsync(process.StandardError, log, logGate, authorizedSession);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            operationGate.Release();
        }
    }

    private static MinecraftLauncher CreateLauncher(string path, string? javaPath = null)
    {
        var parameters = MinecraftLauncherParameters.CreateDefault(new MinecraftPath(path));
        if (parameters.VersionLoader is MojangJsonVersionLoaderV2 loader)
            loader.UseLocalManifestWhenError = true;

        // Respect an explicitly selected Java instead of also downloading another runtime.
        if (!string.IsNullOrWhiteSpace(javaPath))
        {
            foreach (var extractor in parameters.FileExtractors!.OfType<JavaFileExtractor>().ToArray())
                parameters.FileExtractors!.Remove(extractor);
        }
        return new MinecraftLauncher(parameters);
    }

    private static async Task InstallCoreAsync(MinecraftLauncher launcher, string versionId,
        IProgress<LaunchProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new LaunchProgress("Проверка версии и файлов…"));
        var versions = await launcher.GetAllVersionsAsync(cancellationToken).ConfigureAwait(false);
        if (!versions.Any(version => version.Name == versionId && version.Type is "release" or "snapshot"))
            throw new InvalidOperationException("Этой версии нет в официальном списке Minecraft. Обновите список версий.");

        var marker = Path.Combine(launcher.MinecraftPath.BasePath, MarkerName);
        // A failed repair must never leave an old success marker behind.
        File.Delete(marker);
        var reporter = new InstallProgressReporter(progress);
        await launcher.InstallAsync(versionId,
            new SyncProgress<InstallerProgressChangedEventArgs>(reporter.ReportFile),
            new SyncProgress<ByteProgress>(reporter.ReportBytes),
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(launcher.MinecraftPath.BasePath);
        var temporaryMarker = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporaryMarker, versionId, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryMarker, marker, true);
        }
        finally
        {
            try { File.Delete(temporaryMarker); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        progress.Report(new LaunchProgress("Minecraft установлен", 100));
    }

    private string GetGamePath(GameInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Guid.TryParse(instance.Id, out var instanceId))
            throw new ArgumentException("Некорректный идентификатор сборки.", nameof(instance));
        return Path.Combine(dataDirectory, "instances", instanceId.ToString("N"), "game");
    }

    private static GameInstance SnapshotAndValidate(GameInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!Guid.TryParse(instance.Id, out var instanceId))
            throw new ArgumentException("Некорректный идентификатор сборки.", nameof(instance));
        ValidateVersionId(instance.VersionId);
        if (instance.MemoryMb is < 1024 or > 32768)
            throw new ArgumentOutOfRangeException(nameof(instance), "Память должна быть от 1024 до 32768 МБ.");
        var javaPath = instance.JavaPath?.Trim() ?? "";
        if (javaPath.Length > 0 && (!Path.IsPathFullyQualified(javaPath) || !File.Exists(javaPath)))
            throw new ArgumentException("Укажите полный путь к существующему исполняемому файлу Java.", nameof(instance));
        return new GameInstance
        {
            Id = instanceId.ToString("N"),
            Name = instance.Name,
            VersionId = instance.VersionId,
            MemoryMb = instance.MemoryMb,
            JavaPath = javaPath
        };
    }

    private static void ValidateVersionId(string versionId)
    {
        if (string.IsNullOrWhiteSpace(versionId) || versionId.Length > 128 || versionId is "." or ".."
            || versionId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || versionId.Contains('/') || versionId.Contains('\\')
            || versionId.EndsWith('.') || versionId != versionId.Trim())
            throw new ArgumentException("Выберите корректную версию Minecraft.", nameof(versionId));
    }

    private static bool IsNonEmptyFile(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static MSession CopyAuthorizedSession(MSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        // MSession.CheckIsValid also accepts CmlLib's placeholder offline sessions. Reject those here.
        // AuthService obtains this session only after Microsoft/Minecraft profile verification.
        if (!session.CheckIsValid() || session.UserType != "msa"
            || !Guid.TryParse(session.UUID, out _)
            || session.AccessToken is "access_token" or "0")
            throw new InvalidOperationException("Для запуска войдите в Microsoft-аккаунт с Minecraft Java Edition.");
        return new MSession(session.Username, session.AccessToken, session.UUID)
        {
            UserType = session.UserType,
            ClientToken = session.ClientToken,
            Xuid = session.Xuid
        };
    }

    private static async Task ReadLogAsync(StreamReader reader, Action<string> log, object logGate, MSession session)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            var safeLine = GameLogRedactor.Redact(line, session.AccessToken, session.ClientToken);
            lock (logGate)
            {
                // A closed UI/log view must not stop draining the child process's output.
                try { log(safeLine); }
                catch (Exception) { }
            }
        }
    }

    private sealed class InstallProgressReporter(IProgress<LaunchProgress> target)
    {
        private readonly object gate = new();
        private long lastReport;
        private string fileName = "файлы игры";
        private double? percent;

        public void ReportFile(InstallerProgressChangedEventArgs value)
        {
            lock (gate)
            {
                if (!string.IsNullOrWhiteSpace(value.Name))
                    fileName = Path.GetFileName(value.Name);
                Report();
            }
        }

        public void ReportBytes(ByteProgress value)
        {
            lock (gate)
            {
                percent = value.TotalBytes > 0
                    ? Math.Clamp(value.ProgressedBytes * 100d / value.TotalBytes, 0, 100)
                    : null;
                Report();
            }
        }

        private void Report()
        {
            var now = Stopwatch.GetTimestamp();
            if (lastReport != 0 && Stopwatch.GetElapsedTime(lastReport, now).TotalMilliseconds < 150)
                return;
            lastReport = now;
            target.Report(new LaunchProgress($"Проверка и загрузка: {fileName}", percent));
        }
    }
}
