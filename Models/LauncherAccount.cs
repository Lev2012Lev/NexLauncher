namespace NexLauncher.Models;

/// <summary>Public profile information only; credentials never enter the UI model.</summary>
public sealed record LauncherAccount(string Id, string Username, string Uuid, string? SkinUrl = null);
