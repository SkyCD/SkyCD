namespace SkyCD.App.Models;

public sealed class AppOptions
{
    public int? WindowLeft { get; set; }

    public int? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool IsStatusBarVisible { get; set; } = true;

    public string BrowserViewMode { get; set; } = "Details";

    public string BrowserSortMode { get; set; } = "Name";

    public string PluginPath { get; set; } = string.Empty;

    public string Language { get; set; } = "English";
}
