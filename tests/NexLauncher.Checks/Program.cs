using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CmlLib.Core.Auth;
using NexLauncher;
using NexLauncher.Models;
using NexLauncher.Services;
using NexLauncher.ViewModels;

internal static class Program
{
    private static int _checks;
    private static int _result;
    private static readonly string Root = Path.GetFullPath(Path.Combine(".artifacts", "checks", Guid.NewGuid().ToString("N")));

    [STAThread]
    public static int Main(string[] args)
    {
        Directory.CreateDirectory(Root);
        AppBuilder.Configure<App>().UseSkia().UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont().SetupWithoutStarting();
        SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext());
        using var stop = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await CheckStorage();
                await CheckViewModel();
                await CheckBackendGuards();
                await RenderWindow();
                if (args.Contains("--network")) await CheckOfficialCatalogue();
                if (args.Contains("--install-smoke")) await CheckInstallation();
                Console.WriteLine($"PASS: {_checks} checks. Screenshots: {Root}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                _result = 1;
            }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        return _result;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + description);
        _checks++;
        Console.WriteLine("PASS: " + description);
    }

    private static async Task Rejects<T>(Func<Task> action, string description) where T : Exception
    {
        try { await action(); }
        catch (T) { Check(true, description); return; }
        throw new InvalidOperationException("FAILED: expected " + typeof(T).Name + ": " + description);
    }

    private static ConfigurationStore Store(string name) => new(Path.Combine(Root, name));

    private static async Task CheckStorage()
    {
        var store = Store("storage");
        Check((await store.LoadAsync()).Instances.Count == 0, "first launch has no invented installations");
        var instance = new GameInstance { Name = "Выживание", VersionId = "1.21.1", MemoryMb = 6144 };
        var config = new LauncherConfiguration { Instances = [instance], SelectedInstanceId = instance.Id };
        await store.SaveAsync(config);
        var loaded = await store.LoadAsync();
        Check(loaded.Instances.Single().MemoryMb == 6144 && loaded.SelectedInstanceId == instance.Id, "settings round-trip");
        instance.Id = "../outside";
        await Rejects<InvalidDataException>(() => store.SaveAsync(config), "instance path traversal rejected");
        instance.Id = Guid.NewGuid().ToString("N");
        instance.VersionId = "../outside";
        await Rejects<InvalidDataException>(() => store.SaveAsync(config), "version path traversal rejected");
        instance.VersionId = "1.21.1";
        instance.MemoryMb = 50000;
        await Rejects<InvalidDataException>(() => store.SaveAsync(config), "invalid memory rejected");
        instance.MemoryMb = 4096;
        config.MicrosoftClientId = "secret-is-not-a-client-id";
        await Rejects<InvalidDataException>(() => store.SaveAsync(config), "invalid public client ID rejected");
        config.MicrosoftClientId = "";
        await File.WriteAllTextAsync(store.ConfigurationPath, "{broken");
        await Rejects<InvalidDataException>(() => store.LoadAsync(), "corrupt settings reported");
        await Rejects<InvalidDataException>(() => store.SaveAsync(config), "corrupt source cannot be silently overwritten");
        Check(await File.ReadAllTextAsync(store.ConfigurationPath) == "{broken", "corrupt original retained");
        var cancelled = new CancellationToken(true);
        await Rejects<OperationCanceledException>(() => Store("cancel-save").SaveAsync(config, cancelled), "cancelled save leaves no file");
        Check(!File.Exists(Store("cancel-save").ConfigurationPath), "cancelled save does not persist");
    }

    private static async Task CheckViewModel()
    {
        var game = new FakeMinecraft();
        var accounts = new FakeAccounts();
        var store = Store("viewmodel");
        var vm = new MainWindowViewModel(store, game, accounts);
        await vm.InitializeAsync();
        Check(vm.Versions.Count == 2 && vm.SelectedVersion?.Id == "1.21.1", "release list sorted by date, snapshots hidden");
        vm.IncludeSnapshots = true;
        Check(vm.Versions.Count == 3, "snapshot filter opt-in");
        vm.SelectedVersion = vm.Versions.Single(x => x.Type == "snapshot");
        vm.IncludeSnapshots = false;
        Check(vm.SelectedVersion?.Type == "release", "hidden snapshot selection resets safely");
        vm.NewInstanceName = "Моя Vanilla";
        await vm.CreateInstanceCommand.ExecuteAsync(null);
        Check(vm.Instances.Count == 1 && vm.IsEditable && !vm.IsWorking, "creating profile finishes and unlocks UI");
        Check((await store.LoadAsync()).Instances.Single().VersionId == "1.21.1", "profile pins concrete version");
        await vm.PrimaryCommand.ExecuteAsync(null);
        Check(game.InstallCalls == 1 && accounts.SignInCalls == 0 && vm.IsInstalled, "installation works without account");
        await vm.PrimaryCommand.ExecuteAsync(null);
        Check(game.LaunchCalls == 0 && vm.CurrentPage == "settings" && vm.HasError, "launch requires configured Microsoft login");
        vm.ClientId = Guid.NewGuid().ToString();
        vm.MemoryGb = 6;
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Check((await store.LoadAsync()).Instances.Single().MemoryMb == 6144, "saved RAM reaches profile");
        await vm.PrimaryCommand.ExecuteAsync(null);
        Check(game.LaunchCalls == 1 && accounts.SignInCalls == 1 && vm.HasAccount && !vm.IsWorking, "authorized launch and exit reset UI");

        // A running game prevents a second launch or an edit; cancellation never kills it.
        game.HoldLaunch = true;
        var running = vm.PrimaryCommand.ExecuteAsync(null);
        await Until(() => vm.IsGameRunning);
        Check(!vm.PrimaryCommand.CanExecute(null) && !vm.SaveSettingsCommand.CanExecute(null), "running game blocks launch/settings");
        Check(!vm.CancelCommand.CanExecute(null), "running game has no destructive cancel");
        vm.OnWindowClosing();
        Check(!game.LaunchToken.IsCancellationRequested, "closing launcher does not cancel running game");
        game.FinishLaunch.TrySetResult(0);
        await running;
        game.HoldLaunch = false;
        var signOutCalls = accounts.SignOutCalls;
        await vm.SignOutCommand.ExecuteAsync(null);
        Check(!vm.HasAccount && accounts.SignOutCalls == signOutCalls + 1, "sign-out clears UI session");

        vm.NewInstanceName = "Вторая сборка";
        await vm.CreateInstanceCommand.ExecuteAsync(null);
        var secondId = vm.SelectedInstance!.Id;
        vm.SelectedInstance = vm.Instances[0];
        await UntilAsync(async () => (await store.LoadAsync()).SelectedInstanceId == vm.SelectedInstance.Id);
        Check((await store.LoadAsync()).SelectedInstanceId != secondId, "selected profile persists without pressing Play");
        vm.SelectedInstance = vm.Instances[1];
        game.HoldInstall = true;
        game.InstallStarted = false;
        var installing = vm.PrimaryCommand.ExecuteAsync(null);
        await Until(() => game.InstallStarted);
        Check(vm.CancelCommand.CanExecute(null) && !vm.CreateInstanceCommand.CanExecute(null), "install exposes cancel and locks editing");
        vm.CancelCommand.Execute(null);
        await installing;
        Check(vm.IsEditable && !vm.IsInstalled, "cancelled installation resets UI without fake installed state");

        // A failed persistence operation must roll RAM back in memory as well as preserve the original file.
        var oldMemory = vm.SelectedInstance.MemoryMb;
        vm.MemoryGb = 10;
        await File.WriteAllTextAsync(store.ConfigurationPath, "{broken");
        await vm.SaveSettingsCommand.ExecuteAsync(null);
        Check(vm.HasError && vm.SelectedInstance.MemoryMb == oldMemory && vm.IsEditable, "save failure rolls back edits and unlocks UI");
        var broken = new MainWindowViewModel(store, game, accounts);
        await broken.InitializeAsync();
        Check(broken.HasError && !broken.PrimaryCommand.CanExecute(null), "corrupt config cannot trigger install or overwrite");
        Check(LauncherLog.Sanitize("Bearer sample-secret access_token=second-secret").Contains("sample-secret") == false, "log hides bearer secrets");
        Check(!LauncherLog.Sanitize("access_token=second-secret").Contains("second-secret"), "log hides named tokens");
    }

    private static async Task CheckBackendGuards()
    {
        var service = new MinecraftService(Path.Combine(Root, "backend"));
        var instance = new GameInstance { VersionId = "1.21.1" };
        var progress = new Progress<LaunchProgress>();
        Check(!service.IsInstalled(instance), "real backend does not invent installed versions");
        instance.Id = "../escape";
        Check(!service.IsInstalled(instance), "installed check rejects unsafe directory");
        await Rejects<ArgumentException>(() => service.InstallAsync(instance, progress, default), "install rejects unsafe directory before network");
        instance.Id = Guid.NewGuid().ToString("N");
        instance.MemoryMb = 0;
        await Rejects<ArgumentOutOfRangeException>(() => service.InstallAsync(instance, progress, default), "install rejects invalid RAM before network");
        instance.MemoryMb = 4096;
        await Rejects<InvalidOperationException>(() => service.LaunchAsync(instance, MSession.CreateOfflineSession("Guest"), progress, _ => { }, default), "full launch rejects unauthenticated placeholder session");
        using var auth = new CancellationTokenSource();
        auth.Cancel();
        await Rejects<OperationCanceledException>(() => service.GetVersionsAsync(auth.Token), "catalogue honors cancellation");
        var login = new MicrosoftAccountService();
        Check(await login.RestoreAsync("", default) is null, "fresh login service has no persisted token");
        await Rejects<InvalidOperationException>(() => login.SignInAsync("", default), "missing client ID rejected before opening browser");
        await login.SignOutAsync();
    }

    private static async Task RenderWindow()
    {
        var store = Store("render");
        var profile = new GameInstance { Name = "Моя Vanilla", VersionId = "1.21.1" };
        await store.SaveAsync(new LauncherConfiguration { Instances = [profile], SelectedInstanceId = profile.Id });
        var game = new FakeMinecraft();
        game.Installed.Add(profile.Id);
        var vm = new MainWindowViewModel(store, game, new FakeAccounts());
        await vm.InitializeAsync();
        var window = new MainWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame())
        {
            Check(frame is not null, "Avalonia renders real main window");
            frame!.Save(Path.Combine(Root, "play.png"), PngBitmapEncoderOptions.Default);
        }
        vm.CurrentPage = "instances";
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Root, "instances.png"), PngBitmapEncoderOptions.Default);
        vm.CurrentPage = "settings";
        window.Width = 900;
        window.Height = 650;
        Dispatcher.UIThread.RunJobs();
        using (var frame = window.CaptureRenderedFrame()) frame!.Save(Path.Combine(Root, "settings-minimum.png"), PngBitmapEncoderOptions.Default);
        Check(window.DataContext == vm && window.Bounds.Width >= 900, "window uses bound viewmodel at minimum size");
        window.Close();
    }

    private static async Task CheckInstallation()
    {
        // Explicit opt-in: downloads a vanilla client and Mojang Java to an isolated test directory.
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var service = new MinecraftService(Path.Combine(Root, "install-smoke"));
        var profile = new GameInstance { Name = "Installation smoke check", VersionId = "1.21.1" };
        var lastPrinted = DateTimeOffset.MinValue;
        var progress = new Progress<LaunchProgress>(value =>
        {
            if ((DateTimeOffset.UtcNow - lastPrinted).TotalSeconds < 5) return;
            lastPrinted = DateTimeOffset.UtcNow;
            Console.WriteLine("INSTALL: " + value.Message + (value.Percent is { } p ? $" ({p:F0}%)" : ""));
        });
        await service.InstallAsync(profile, progress, timeout.Token);
        Check(service.IsInstalled(profile), "real vanilla install downloads client, assets, libraries and Java");
        var gameDirectory = Path.Combine(Root, "install-smoke", "instances", profile.Id, "game");
        var clientJar = Path.Combine(gameDirectory, "versions", profile.VersionId, profile.VersionId + ".jar");
        File.Move(clientJar, clientJar + ".test-backup");
        Check(!service.IsInstalled(profile), "missing client JAR invalidates installation state");
        await service.InstallAsync(profile, progress, timeout.Token);
        Check(service.IsInstalled(profile), "real reinstall repairs missing client file");
    }

    private static async Task CheckOfficialCatalogue()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var service = new MinecraftService(Path.Combine(Root, "network"));
        var versions = await service.GetVersionsAsync(timeout.Token);
        Check(versions.Count > 0 && versions.Any(x => x.Type == "release"), "real Mojang version catalogue loads");
    }

    private static async Task Until(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++) await Task.Delay(10);
        Check(predicate(), "async operation reached expected state");
    }
    private static async Task UntilAsync(Func<Task<bool>> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            if (await predicate()) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Async condition was not reached.");
    }
}

internal sealed class FakeMinecraft : IMinecraftService
{
    public readonly HashSet<string> Installed = new();
    public int InstallCalls, LaunchCalls;
    public bool HoldInstall, InstallStarted, HoldLaunch;
    public CancellationToken LaunchToken;
    public TaskCompletionSource<int> FinishLaunch = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<IReadOnlyList<MinecraftRelease>> GetVersionsAsync(CancellationToken token) =>
        Task.FromResult<IReadOnlyList<MinecraftRelease>>([
            new("1.20.1", "release", DateTimeOffset.Parse("2023-06-12")),
            new("snapshot-test", "snapshot", DateTimeOffset.Parse("2025-01-01")),
            new("1.21.1", "release", DateTimeOffset.Parse("2024-08-08"))]);
    public bool IsInstalled(GameInstance instance) => Installed.Contains(instance.Id);
    public async Task InstallAsync(GameInstance instance, IProgress<LaunchProgress> progress, CancellationToken token)
    {
        InstallCalls++;
        InstallStarted = true;
        progress.Report(new LaunchProgress("Загрузка ресурсов", 30));
        if (HoldInstall) await Task.Delay(Timeout.Infinite, token);
        token.ThrowIfCancellationRequested();
        Installed.Add(instance.Id);
    }
    public async Task<int> LaunchAsync(GameInstance instance, MSession session, IProgress<LaunchProgress> progress, Action<string> log, CancellationToken token)
    {
        LaunchCalls++;
        LaunchToken = token;
        progress.Report(new LaunchProgress("Игра запущена", null, true));
        log("Test game started.");
        return HoldLaunch ? await FinishLaunch.Task : 0;
    }
}

internal sealed class FakeAccounts : IAccountService
{
    public int SignInCalls, SignOutCalls;
    private MSession? _session;
    public string? PlayerName => _session?.Username;
    public Task<MSession> SignInAsync(string id, CancellationToken token)
    {
        SignInCalls++;
        _session = new MSession("TestPlayer", "test-access-token", Guid.NewGuid().ToString("N")) { UserType = "msa" };
        return Task.FromResult(_session);
    }
    public Task<MSession?> RestoreAsync(string id, CancellationToken token) => Task.FromResult(_session);
    public Task SignOutAsync() { SignOutCalls++; _session = null; return Task.CompletedTask; }
}
