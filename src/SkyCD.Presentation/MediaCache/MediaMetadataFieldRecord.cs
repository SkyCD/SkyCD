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

    public static MediaMetadataFieldRecord From(MediaMetadataField field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return new MediaMetadataFieldRecord
        {
            Key = field.Key,
            Type = field.Type,
            Unit = field.Unit
        };
    }

    public MediaMetadataField ToField() => new(Key, Type, Unit);
}
