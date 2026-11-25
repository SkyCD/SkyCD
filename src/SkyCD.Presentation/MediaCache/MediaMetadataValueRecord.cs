using System;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Persisted form of <see cref="MediaMetadataValue"/>. Kept as a separate writable type so captured
/// values survive storage round-trips without depending on plugin contract construction rules.
/// </summary>
public sealed class MediaMetadataValueRecord
{
    public string Key { get; set; } = string.Empty;

    public MediaMetadataType Type { get; set; }

    public string? Text { get; set; }

    public long? Integer { get; set; }

    public double? Number { get; set; }

    public bool? Boolean { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public static MediaMetadataValueRecord From(MediaMetadataValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return new MediaMetadataValueRecord
        {
            Key = value.Key,
            Type = value.Type,
            Text = value.Text,
            Integer = value.Integer,
            Number = value.Number,
            Boolean = value.Boolean,
            Timestamp = value.Timestamp
        };
    }

    public MediaMetadataValue ToValue() => new(Key, Type, Text, Integer, Number, Boolean, Timestamp);

    /// <summary>The single payload that matches <see cref="Type"/>, or null when unset.</summary>
    public object? GetPayload() => Type switch
    {
        MediaMetadataType.Text => Text,
        MediaMetadataType.Integer => Integer,
        MediaMetadataType.Number => Number,
        MediaMetadataType.Boolean => Boolean,
        MediaMetadataType.Timestamp => Timestamp,
        _ => null
    };
}
