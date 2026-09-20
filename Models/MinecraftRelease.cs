using System;

namespace NexLauncher.Models;

public sealed record MinecraftRelease(string Id, string Type, DateTimeOffset ReleasedAt)
{
    public string DisplayName => Type == "snapshot" ? $"{Id} · снапшот" : Id;
    public override string ToString() => DisplayName;
}
