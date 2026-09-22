namespace NexLauncher.Models;

public enum AccountType
{
    Microsoft,
    Local
}

/// <summary>Public profile information only; credentials never enter the UI model.</summary>
public sealed record LauncherAccount(
    string Id, string Username, string Uuid, string? SkinUrl = null, AccountType Type = AccountType.Microsoft)
{
    public string TypeLabel => Type == AccountType.Local ? "Локальный" : "Microsoft";
    public string DisplayName => $"{Username} · {TypeLabel}";
}
