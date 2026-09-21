using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CmlLib.Core.Auth;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexLauncher.Models;
using NexLauncher.Services;

namespace NexLauncher.ViewModels;

/// <summary>Public account presentation only. Credentials never enter bindings.</summary>
public partial class AccountsViewModel : ObservableObject, IDisposable
{
    private readonly IAccountService _service;
    private readonly Func<Func<CancellationToken, Task>, Task> _run;

    private readonly Action _changed;
    private CancellationTokenSource? _avatarRequest;
    private bool _disposed;

    public ObservableCollection<LauncherAccount> Items { get; } = new();
    [ObservableProperty] private LauncherAccount? _selectedAccount;
    [ObservableProperty] private string _status = "Войди в Microsoft, чтобы запускать Minecraft.";
    [ObservableProperty] private CroppedBitmap? _avatar;
    public bool HasAccount => _service.ActiveAccountId is not null;
    public bool HasNoAccount => !HasAccount;
    public string Username => _service.PlayerName ?? "Без аккаунта";
    public string Initial => HasAccount ? Username[..1].ToUpperInvariant() : "N";
    public bool HasAvatar => Avatar is not null;
    public bool HasNoAvatar => !HasAvatar;
    public IAsyncRelayCommand AddCommand { get; }
    public IAsyncRelayCommand ActivateCommand { get; }
    public IAsyncRelayCommand RemoveCommand { get; }

    public AccountsViewModel(IAccountService service, Func<Func<CancellationToken, Task>, Task> run,
        Func<bool> canEdit, Action changed)
    {
        _service = service; _run = run; _changed = changed;
        AddCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            Status = "Вход Microsoft… Закрой окно входа или нажми «Отменить», чтобы отменить.";
            await _service.SignInAsync(token);
            Refresh();
            Status = "Аккаунт сохранён. Сессия обновляется перед запуском игры.";
        }), canEdit);
        ActivateCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            if (SelectedAccount is not { } selected) return;
            await _service.SelectAccountAsync(selected.Id, token);
            Refresh();
            Status = "Активный аккаунт: " + Username;
        }), () => canEdit() && SelectedAccount is not null && SelectedAccount.Id != _service.ActiveAccountId);
        RemoveCommand = new AsyncRelayCommand(() => RunAsync(async token =>
        {
            if (SelectedAccount is not { } selected) return;
            await _service.RemoveAccountAsync(selected.Id, token);
            Refresh();
            Status = "Аккаунт удалён с этого устройства.";
        }), () => canEdit() && SelectedAccount is not null);
    }

    private Task RunAsync(Func<CancellationToken, Task> action) => _run(async token =>
    {
        try { await action(token); }
        catch (OperationCanceledException) { Status = "Вход отменён."; throw; }
        catch (Exception ex) { Status = ex.Message; throw; }
    });

    public async Task InitializeAsync(CancellationToken token = default)
    {
        await _service.InitializeAsync(token);
        Refresh();
        if (HasAccount) Status = "Аккаунт сохранён. Вход проверится при запуске.";
    }

    public async Task<MSession?> GetSessionAsync(CancellationToken token)
    {
        Status = HasAccount ? "Обновляем сессию Minecraft…" : "Вход Microsoft…";
        var session = await _service.RestoreAsync(token) ?? await _service.SignInAsync(token);
        Refresh();
        Status = "Minecraft: Java Edition · " + Username;
        return session;
    }

    public void SetError(string message) => Status = message;
    partial void OnSelectedAccountChanged(LauncherAccount? value) => RefreshCommands();
    partial void OnAvatarChanged(CroppedBitmap? value)
    {
        OnPropertyChanged(nameof(HasAvatar)); OnPropertyChanged(nameof(HasNoAvatar));
    }
    public void RefreshCommands()
    {
        AddCommand?.NotifyCanExecuteChanged();
        ActivateCommand?.NotifyCanExecuteChanged();
        RemoveCommand?.NotifyCanExecuteChanged();
    }

    private void Refresh()
    {
        Items.Clear();
        foreach (var account in _service.Accounts) Items.Add(account);
        SelectedAccount = Items.FirstOrDefault(x => x.Id == _service.ActiveAccountId);
        foreach (var name in new[] { nameof(Username), nameof(Initial), nameof(HasAccount), nameof(HasNoAccount) })
            OnPropertyChanged(name);
        RefreshCommands();
        _changed();
        _avatarRequest?.Cancel();
        _avatarRequest?.Dispose();
        _avatarRequest = new CancellationTokenSource();
        _ = LoadAvatarAsync(SelectedAccount?.SkinUrl, _avatarRequest.Token);
    }

    private async Task LoadAvatarAsync(string? url, CancellationToken token)
    {
        var old = Avatar;
        Avatar = null;
        (old?.Source as IDisposable)?.Dispose();
        var image = await MinecraftAvatar.LoadAsync(url, token);
        if (_disposed || token.IsCancellationRequested) { (image?.Source as IDisposable)?.Dispose(); return; }
        Avatar = image;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _avatarRequest?.Cancel(); _avatarRequest?.Dispose();
        (Avatar?.Source as IDisposable)?.Dispose();
        Avatar = null;
    }
}
