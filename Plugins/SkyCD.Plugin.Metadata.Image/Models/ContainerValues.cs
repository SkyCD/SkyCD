using System;
using System.Collections.Generic;
using SkyCD.Plugin.MediaMetadata.Models;
using SkyCD.Plugin.Metadata.Image.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Image.Models;

internal sealed class ContainerValues(string format)
{
    public List<MediaMetadataValue> Values { get; } = [new("Format", MediaMetadataType.Text, Text: format)];

    public void Text(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Values.Add(new(key, MediaMetadataType.Text, Text: value.Trim()));
        }
    }

    public void Integer(string key, long? value)
    {
        if (value.HasValue)
        {
            Values.Add(new(key, MediaMetadataType.Integer, Integer: value));
        }
    }

    public void Size(long width, long height)
    {
        Integer("Width", width > 0 ? width : null);
        Integer("Height", height > 0 ? height : null);
    }

    public void Timestamp(string key, DateTimeOffset? value)
    {
        if (value.HasValue)
        {
            Values.Add(new(key, MediaMetadataType.Timestamp, Timestamp: value));
        }
    }

    public void AddEmbedded(EmbeddedMetadata embedded)
    {
        Timestamp("CapturedAt", embedded.CapturedAt);
        Values.AddRange(MetadataValues.Build(embedded.Tags, ImageSchema.Tags, ImageSchema.Comment, ImageSchema.Rating));
    }
}
