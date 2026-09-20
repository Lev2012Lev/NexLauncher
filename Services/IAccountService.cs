using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;

namespace NexLauncher.Services;

public interface IAccountService
{
    string? PlayerName { get; }
    Task<MSession> SignInAsync(string clientId, CancellationToken cancellationToken);
    Task<MSession?> RestoreAsync(string clientId, CancellationToken cancellationToken);
    Task SignOutAsync();
}
