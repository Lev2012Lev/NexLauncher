using System.Text.Json.Nodes;

namespace NexLauncher.Services.Authentication;

/// <summary>Private authentication state. Never pass this object to logging or UI code.</summary>
public sealed record AccountVaultSnapshot(JsonObject Accounts, string? ActiveAccountId)
{
    public static AccountVaultSnapshot Empty() => new(new JsonObject(), null);
    public AccountVaultSnapshot Copy() => new((JsonObject)Accounts.DeepClone(), ActiveAccountId);
}
