namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Represents a plugin item in the Options dialog.
/// </summary>
public sealed record OptionsPluginItem(
    string Name,
    string Type,
    string ExtendedInfo,
    bool SupportsConfiguration = false,
    bool IsEnabled = true);
