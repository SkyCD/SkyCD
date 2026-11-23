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
        string? primaryExtension = null,
        string? projectUrl = null,
        string? version = null,
        string? capabilityType = null,
        long? sizeBytes = null,
        PluginUpdateSource? updateSource = null)
    {
        Name = name;
        Type = type;
        AuthorUrl = authorUrl;
        ExtendedInfo = extendedInfo;
        this.isEnabled = isEnabled;
        Id = string.IsNullOrWhiteSpace(id) ? name : id;
        Description = description ?? string.Empty;
        PrimaryExtension = string.IsNullOrWhiteSpace(primaryExtension) ? null : primaryExtension;
        ProjectUrl = string.IsNullOrWhiteSpace(projectUrl) ? null : projectUrl;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
        CapabilityType = string.IsNullOrWhiteSpace(capabilityType) ? null : capabilityType;
        SizeBytes = sizeBytes;
        UpdateSource = updateSource;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Author/vendor name, with the caller's unknown-author fallback already applied.</summary>
    public string Type { get; }

    public string? AuthorUrl { get; }

    /// <summary>Project homepage declared by the plugin manifest.</summary>
    public string? ProjectUrl { get; }

    /// <summary>Plugin version, when the host could resolve it.</summary>
    public string? Version { get; }

    /// <summary>Implemented capability types, when the plugin assembly could be inspected.</summary>
    public string? CapabilityType { get; }

    /// <summary>On-disk size of the plugin folder; <c>null</c> when no file location is known.</summary>
    public long? SizeBytes { get; }

    /// <summary>Update channel when the install state is known; <c>null</c> for stale descriptors.</summary>
    public PluginUpdateSource? UpdateSource { get; }

    /// <summary>Primary format extension without a leading dot, when the plugin declares one.</summary>
    public string? PrimaryExtension { get; }

    public string? IconKey => PrimaryExtension is null ? null : $"ext:{PrimaryExtension}";

    public bool HasIcon => IconKey is not null;

    public string ExtendedInfo { get; }

    public string Description { get; }

    [ObservableProperty] private bool isEnabled;
}
