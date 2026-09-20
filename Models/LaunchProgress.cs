namespace NexLauncher.Models;

public sealed record LaunchProgress(string Message, double? Percent = null, bool IsRunning = false);
