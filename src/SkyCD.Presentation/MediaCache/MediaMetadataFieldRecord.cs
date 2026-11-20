using System;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Persisted form of <see cref="MediaMetadataField"/>. The schema travels with the record so captured
/// metadata stays describable offline, after the producing plugin is disabled or uninstalled.
/// </summary>
public sealed class MediaMetadataFieldRecord
{
    public string Key { get; set; } = string.Empty;

    public MediaMetadataType Type { get; set; }

    public string? Unit { get; set; }

    /// <summary>
    /// Persisted <see cref="MediaMetadataField.Role"/>, so a display name or tag value captured while an
    /// option was enabled stays recognizable after the plugin or option changes.
    /// </summary>
    public MediaMetadataFieldRole Role { get; set; }

    public static MediaMetadataFieldRecord From(MediaMetadataField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new MediaMetadataFieldRecord
        {
            Key = field.Key,
            Type = field.Type,
            Unit = field.Unit,
            Role = field.Role
        };
    }

    public MediaMetadataField ToField() => new(Key, Type, Unit, Role);
}
