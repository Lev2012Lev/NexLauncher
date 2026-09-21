using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using NexLauncher.Models;

namespace NexLauncher.Services;

public interface IAccountService
{
    IReadOnlyList<LauncherAccount> Accounts { get; }
    string? ActiveAccountId { get; }
    string? PlayerName { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<MSession> SignInAsync(CancellationToken cancellationToken);
    Task<MSession?> RestoreAsync(CancellationToken cancellationToken);
    Task SelectAccountAsync(string id, CancellationToken cancellationToken);
    Task RemoveAccountAsync(string id, CancellationToken cancellationToken);
    /// <summary>Removes the active account and its local credentials; other accounts remain saved.</summary>
    Task SignOutAsync();
}
