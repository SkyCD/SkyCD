using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Represents a plugin item in the Options dialog.
/// </summary>
public sealed partial class OptionsPluginItem : ObservableObject
{
    public OptionsPluginItem(
        string name,
        string type,
        string extendedInfo,
        bool supportsConfiguration = false,
        bool isEnabled = true,
        string? id = null)
    {
        Name = name;
        Type = type;
        ExtendedInfo = extendedInfo;
        SupportsConfiguration = supportsConfiguration;
        this.isEnabled = isEnabled;
        Id = string.IsNullOrWhiteSpace(id) ? name : id;
    }

    public string Id { get; }

    public string Name { get; }

    public string Type { get; }

    public string ExtendedInfo { get; }

    public bool SupportsConfiguration { get; }

    [ObservableProperty]
    private bool isEnabled;
}
