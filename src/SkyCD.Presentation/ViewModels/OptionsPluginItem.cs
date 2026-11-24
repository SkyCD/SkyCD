using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Presentation.Plugins;

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
        PluginUpdateSource? updateSource = null,
        string? icon = null,
        PluginIconKind iconKind = PluginIconKind.Feature)
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
        DeclaredIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        IconKind = iconKind;
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

    /// <summary>Icon declared by the plugin, or <see langword="null"/> when the kind fallback applies.</summary>
    public string? DeclaredIcon { get; }

    /// <summary>Capability family that supplies the fallback icon.</summary>
    public PluginIconKind IconKind { get; }

    public string IconKey => PluginIconKey.Resolve(DeclaredIcon, IconKind, PrimaryExtension);

    /// <summary>True when the icon key names a Lucide glyph rendered by the icon pack.</summary>
    public bool HasGlyphIcon => PluginIconKey.IsGlyph(IconKey);

    /// <summary>True when the icon key resolves to an image or an OS extension icon.</summary>
    public bool HasBitmapIcon => !HasGlyphIcon;

    public string ExtendedInfo { get; }

    public string Description { get; }

    [ObservableProperty] private bool isEnabled;
}
