using System;
using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.MediaMetadata.Parsing;

namespace SkyCD.Plugin.MediaMetadata.Models;

/// <summary>Turns one scan into the tag, comment and rating values every carrier publishes.</summary>
internal static class MetadataValues
{
    public static IReadOnlyList<MediaMetadataValue> Build(MetadataScan scan, string tagsKey, string commentKey,
        string ratingKey)
    {
        ArgumentNullException.ThrowIfNull(scan);

        var values = new List<MediaMetadataValue>(3);
        if (scan.Keywords.Count > 0)
        {
            values.Add(new MediaMetadataValue(tagsKey, MediaMetadataType.Text,
                Text: string.Join("; ", scan.Keywords)));
        }

        if (scan.Comment is not null)
        {
            values.Add(new MediaMetadataValue(commentKey, MediaMetadataType.Text, Text: scan.Comment));
        }

        if (scan.Rating is { } rating)
        {
            values.Add(new MediaMetadataValue(ratingKey, MediaMetadataType.Integer, Integer: rating));
        }

        return values;
    }
}
