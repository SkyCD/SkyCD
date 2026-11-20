using System;

namespace SkyCD.Plugin.MediaMetadata;

/// <summary>
/// Reads IPTC-IIM records, the keyword/caption format written by Photo Mechanic, XnView, Windows Explorer
/// and many stock agencies. IIM lives either in a Photoshop APP13 resource block or in TIFF tag 33723.
/// </summary>
internal static class IptcMetadataReader
{
    private const ushort ResourceIptcIim = 0x0404;
    private const byte IimMarker = 0x1C;
    private const byte ApplicationRecord = 2;
    private const byte KeywordDataset = 25;
    private const byte CaptionDataset = 120;

    /// <summary>Walks 8BIM resource blocks inside a JPEG APP13 payload.</summary>
    public static void ReadPhotoshopResources(ReadOnlySpan<byte> payload, MetadataScan scan)
    {
        var position = 0;
        while (position + 12 <= payload.Length)
        {
            if (!payload[position..].StartsWith("8BIM"u8))
            {
                // Tolerate alignment padding between resources.
                position++;
                continue;
            }

            var resourceId = (ushort)((payload[position + 4] << 8) | payload[position + 5]);
            position += 6;

            var nameLength = payload[position];
            var nameTotal = 1 + nameLength;
            if ((nameTotal & 1) == 1)
            {
                nameTotal++;
            }

            position += nameTotal;
            if (position + 4 > payload.Length)
            {
                return;
            }

            var size = ReadInt32BigEndian(payload, position);
            position += 4;
            if (size < 0 || position + size > payload.Length)
            {
                return;
            }

            if (resourceId == ResourceIptcIim)
            {
                ReadIim(payload.Slice(position, size), scan);
            }

            position += size;
            if ((size & 1) == 1)
            {
                position++;
            }
        }
    }

    /// <summary>Reads the records themselves; malformed data stops the walk without failing extraction.</summary>
    public static void ReadIim(ReadOnlySpan<byte> data, MetadataScan scan)
    {
        var position = 0;
        while (position + 5 <= data.Length && data[position] == IimMarker)
        {
            var record = data[position + 1];
            var dataset = data[position + 2];
            var length = (data[position + 3] << 8) | data[position + 4];
            position += 5;

            if ((length & 0x8000) != 0)
            {
                var count = length & 0x7FFF;
                if (count is < 1 or > 4 || position + count > data.Length)
                {
                    return;
                }

                long extended = 0;
                for (var index = 0; index < count; index++)
                {
                    extended = (extended << 8) | data[position + index];
                }

                position += count;
                if (extended > int.MaxValue)
                {
                    return;
                }

                length = (int)extended;
            }

            if (length < 0 || position + length > data.Length)
            {
                return;
            }

            var value = data.Slice(position, length);
            position += length;

            if (record != ApplicationRecord)
            {
                continue;
            }

            switch (dataset)
            {
                case KeywordDataset:
                    scan.AddKeyword(MetadataText.Decode(value));
                    break;
                case CaptionDataset:
                    scan.SetComment(MetadataText.Decode(value));
                    break;
            }
        }
    }

    private static int ReadInt32BigEndian(ReadOnlySpan<byte> data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
