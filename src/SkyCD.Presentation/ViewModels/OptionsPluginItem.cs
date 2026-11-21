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
        bool isEnabled = true,
        string? id = null,
        string? authorUrl = null,
        string? description = null,
        string? primaryExtension = null)
    {
        Name = name;
        Type = type;
        AuthorUrl = authorUrl;
        ExtendedInfo = extendedInfo;
        this.isEnabled = isEnabled;
        Id = string.IsNullOrWhiteSpace(id) ? name : id;
        Description = description ?? string.Empty;
        PrimaryExtension = string.IsNullOrWhiteSpace(primaryExtension) ? null : primaryExtension;
    }

    public string Id { get; }

    public string Name { get; }

    public string Type { get; }

    public string? AuthorUrl { get; }

    public bool HasAuthorUrl => !string.IsNullOrWhiteSpace(AuthorUrl);

    /// <summary>Primary format extension without a leading dot, when the plugin declares one.</summary>
    public string? PrimaryExtension { get; }

    public string? IconKey => PrimaryExtension is null ? null : $"ext:{PrimaryExtension}";

    public bool HasIcon => IconKey is not null;

    public string ExtendedInfo { get; }

    public string Description { get; }

    [ObservableProperty] private bool isEnabled;
}