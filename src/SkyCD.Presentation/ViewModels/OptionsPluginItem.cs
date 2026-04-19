namespace SkyCD.Presentation.ViewModels;

public sealed record OptionsPluginItem(
    string Name,
    string Type,
    string ExtendedInfo,
    bool SupportsConfiguration = false,
    bool IsEnabled = true);
