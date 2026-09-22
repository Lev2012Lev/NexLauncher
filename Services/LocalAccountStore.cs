using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NexLauncher.Models;
using NexLauncher.Services.Authentication;

namespace NexLauncher.Services;

/// <summary>Atomic public profile storage. Microsoft credentials never enter this format.</summary>
public sealed class LocalAccountStore : ILocalAccountStore
{
    private const int MaximumBytes = 256 * 1024;
    private const int MaximumAccounts = 256;
    private readonly string _path;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public LocalAccountStore(string path) => _path = Path.GetFullPath(path);

    public Task<LocalAccountSnapshot> LoadAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    public async Task SaveAsync(LocalAccountSnapshot snapshot, CancellationToken cancellationToken)
    {
        var candidate = Validate(snapshot);
        await _writeLock.WaitAsync(cancellationToken);
        try { await Task.Run(() => Write(candidate, cancellationToken), cancellationToken); }
        finally { _writeLock.Release(); }
    }

    private LocalAccountSnapshot Read(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            if (stream.Length is <= 0 or > MaximumBytes) throw new InvalidDataException();
            var root = JsonNode.Parse(stream) as JsonObject;
            if (root is null || root["version"]?.GetValue<int>() != 1 || root["localAccounts"] is not JsonArray profiles)
                throw new InvalidDataException();
            if (profiles.Count > MaximumAccounts) throw new InvalidDataException();
            var accounts = profiles.Select(profile => LocalAccountIdentity.CreateProfile(profile!.GetValue<string>())).ToArray();
            AccountType? selectedType = null;
            string? selectedId = null;
            if (root["activeAccount"] is { } active)
            {
                if (active is not JsonObject selected) throw new InvalidDataException();
                selectedType = selected["type"]?.GetValue<string>() switch
                {
                    "Microsoft" => AccountType.Microsoft,
                    "Local" => AccountType.Local,
                    _ => throw new InvalidDataException()
                };
                selectedId = selected["id"]?.GetValue<string>() ?? throw new InvalidDataException();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Validate(new LocalAccountSnapshot(accounts, selectedType, selectedId));
        }
        catch (FileNotFoundException) { return LocalAccountSnapshot.Empty(); }
        catch (DirectoryNotFoundException) { return LocalAccountSnapshot.Empty(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            throw new AccountStorageException("Не удалось прочитать список локальных аккаунтов и выбранный профиль. Исходный файл auth/profiles.json сохранён. Проверь доступ или исправь повреждённый файл.");
        }
    }

    private void Write(LocalAccountSnapshot snapshot, CancellationToken cancellationToken)
    {
        string? temporaryPath = null;
        try
        {
            // Also protect a file damaged after the launcher initially loaded its profiles.
            _ = Read(cancellationToken);
            var names = new JsonArray();
            foreach (var profile in snapshot.LocalAccounts) names.Add(profile.Username);
            var root = new JsonObject
            {
                ["version"] = 1,
                ["localAccounts"] = names,
                ["activeAccount"] = snapshot.ActiveAccountId is null ? null : new JsonObject
                {
                    ["type"] = snapshot.ActiveAccountType!.Value.ToString(),
                    ["id"] = snapshot.ActiveAccountId
                }
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
            if (bytes.Length > MaximumBytes) throw new InvalidDataException();
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $"profiles.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        catch (OperationCanceledException) { throw; }
        catch (AccountStorageException) { throw; }
        catch (Exception)
        {
            throw new AccountStorageException("Не удалось сохранить локальные аккаунты и выбор профиля. Проверь доступ к папке NexLauncher и свободное место. Предыдущий файл сохранён.");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static LocalAccountSnapshot Validate(LocalAccountSnapshot snapshot)
    {
        if (snapshot.LocalAccounts.Count > MaximumAccounts) throw new InvalidDataException("Слишком много локальных аккаунтов.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var profile in snapshot.LocalAccounts)
        {
            if (profile is null || profile.Type != AccountType.Local || !LocalAccountIdentity.IsValidUsername(profile.Username) ||
                profile != LocalAccountIdentity.CreateProfile(profile.Username) || !ids.Add(profile.Id))
                throw new InvalidDataException("Некорректный локальный профиль.");
        }
        if ((snapshot.ActiveAccountId is null) != (snapshot.ActiveAccountType is null)) throw new InvalidDataException();
        if (snapshot.ActiveAccountId is { } selected)
        {
            if (string.IsNullOrWhiteSpace(selected) || selected.Length > 256 || selected.Any(char.IsControl)) throw new InvalidDataException();
            if (snapshot.ActiveAccountType == AccountType.Local && !ids.Contains(selected)) throw new InvalidDataException();
            if (snapshot.ActiveAccountType == AccountType.Microsoft && selected.StartsWith("local:", StringComparison.Ordinal)) throw new InvalidDataException();
            if (snapshot.ActiveAccountType is not (AccountType.Local or AccountType.Microsoft)) throw new InvalidDataException();
        }
        return snapshot.Copy();
    }
}

