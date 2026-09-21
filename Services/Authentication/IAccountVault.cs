using System.Threading;
using System.Threading.Tasks;

namespace NexLauncher.Services.Authentication;

public interface IAccountVault
{
    Task<AccountVaultSnapshot> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(AccountVaultSnapshot snapshot, CancellationToken cancellationToken);
}
