using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexLauncher.Models;

namespace NexLauncher.Services;

/// <summary>Public data only: local profiles and the selection shared by both account types.</summary>
public sealed record LocalAccountSnapshot(
    IReadOnlyList<LauncherAccount> LocalAccounts, AccountType? ActiveAccountType, string? ActiveAccountId)
{
    public static LocalAccountSnapshot Empty() => new(Array.Empty<LauncherAccount>(), null, null);
    public LocalAccountSnapshot Copy() => new(Array.AsReadOnly(LocalAccounts.ToArray()), ActiveAccountType, ActiveAccountId);
}

public interface ILocalAccountStore
{
    Task<LocalAccountSnapshot> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LocalAccountSnapshot snapshot, CancellationToken cancellationToken);
}
