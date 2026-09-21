using System.Collections.Generic;

namespace NexLauncher.Models;

public sealed class LauncherConfiguration
{
    public List<GameInstance> Instances { get; set; } = new();
    public string? SelectedInstanceId { get; set; }
    public string MicrosoftClientId { get; set; } = "";
    public QuickCssSettings QuickCss { get; set; } = new();
    public bool ShowSnapshots { get; set; }
}
