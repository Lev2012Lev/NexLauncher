using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth;
using NexLauncher.Models;

namespace NexLauncher.Services.Authentication;

/// <summary>Platform-specific OAuth sits behind this boundary; UI never handles credentials.</summary>
public interface IMinecraftAuthenticationBackend
{
    IReadOnlyList<LauncherAccount> GetAccounts(JsonObject storedAccounts);
    Task<MinecraftAuthenticationResult> AuthenticateAsync(
        JsonObject storedAccounts, string? accountId, bool interactive, CancellationToken cancellationToken);
}

/// <summary>Private result, committed to encrypted storage before the public account list changes.</summary>
public sealed record MinecraftAuthenticationResult(MSession Session, JsonObject Accounts);
