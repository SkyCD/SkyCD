using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Roblox place and model files. The binary form is a 32-byte header; compressed chunks are never
/// decompressed. The XML form counts items and classes and reads the place name only.
/// </summary>
internal static class RobloxReader
{
    internal static GameDevFacts? ReadBinary(byte[] data, bool truncated)
    {
        ReadOnlySpan<byte> magic = [0x3C, 0x72, 0x6F, 0x62, 0x6C, 0x6F, 0x78, 0x21, 0x89, 0xFF, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 32 || !GameDevSource.StartsWith(data, magic.ToArray()))
        {
            return null;
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(14, 2));
        var classCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16, 4));
        var instanceCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20, 4));

        return new GameDevFacts
        {
            Family = "Roblox",
            Format = "Roblox binary place",
            Application = "Roblox Studio",
            FormatVersion = version.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ClassCount = classCount,
            InstanceCount = instanceCount,
            Compiled = true,
            ScanIncomplete = truncated
        };
    }

    internal static GameDevFacts? ReadXml(string text, bool truncated)
    {
        var classes = new Dictionary<string, long>(StringComparer.Ordinal);
        string? formatVersion = null;
        string? name = null;
        long items = 0;

        try
        {
            using var reader = GameDevDocuments.CreateXml(text);
            if (!GameDevDocuments.ReadRootElement(reader) || reader.LocalName != "roblox")
            {
                return null;
            }

            formatVersion = reader.GetAttribute("version");
            var depth = reader.Depth;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                {
                    break;
                }

                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                if (reader.LocalName == "Item")
                {
                    items++;
                    var cls = reader.GetAttribute("class") ?? "unknown";
                    classes[cls] = classes.TryGetValue(cls, out var count) ? count + 1 : 1;
                }
                else if (reader.LocalName == "string" && reader.GetAttribute("name") == "Name" && name is null)
                {
                    name = reader.ReadElementContentAsString();
                }
            }
        }
        catch (XmlException)
        {
            return null;
        }

        var facts = new GameDevFacts
        {
            Family = "Roblox",
            Format = "Roblox XML place",
            Application = "Roblox Studio",
            FormatVersion = formatVersion,
            InstanceCount = items,
            ClassCount = classes.Count,
            ProjectName = string.IsNullOrWhiteSpace(name) ? null : name,
            ScanIncomplete = truncated
        };

        foreach (var pair in classes)
        {
            facts.AddNameSample(pair.Key);
            facts.AddPreviewRow(pair.Key + " ×" + pair.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return facts;
    }
}
