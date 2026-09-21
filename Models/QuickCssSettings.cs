namespace NexLauncher.Models;

public sealed class QuickCssSettings
{
    public bool Enabled { get; set; }
    public string FilePath { get; set; } = "";
    public bool AutoReload { get; set; } = true;
}
