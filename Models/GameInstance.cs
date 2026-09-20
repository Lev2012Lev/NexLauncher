using System;

namespace NexLauncher.Models;

public sealed class GameInstance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Моя сборка";
    public string VersionId { get; set; } = "";
    public int MemoryMb { get; set; } = 4096;
    public string JavaPath { get; set; } = "";
    public override string ToString() => Name;
}
