using System;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads O3DE slice XML and asset JSON. Prefab and material documents wait for the shared probe.</summary>
internal static class O3deReader
{
    internal static GameDevFacts? ReadSlice(string text, bool truncated)
    {
        try
        {
            using var reader = GameDevDocuments.CreateXml(text);
            if (!GameDevDocuments.ReadRootElement(reader) || reader.LocalName != "ObjectStream")
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "O3DE",
                Format = "O3DE slice",
                Application = "O3DE",
                FormatVersion = reader.GetAttribute("Version"),
                ScanIncomplete = truncated
            };

            var depth = reader.Depth;
            long classes = 0;
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

                if (reader.GetAttribute("Class") is { Length: > 0 } className)
                {
                    classes++;
                    facts.AddNameSample(className);
                }
                else if (reader.GetAttribute("Name") is { Length: > 0 } name)
                {
                    facts.AddPreviewRow("Entity: " + name);
                }
            }

            facts.ClassCount = classes;
            facts.EntityCount = classes;
            return facts;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    internal static GameDevFacts? ReadAsset(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var type = GameDevDocuments.GetString(root, "$type");
            if (string.IsNullOrWhiteSpace(type))
            {
                return null;
            }

            return new GameDevFacts
            {
                Family = "O3DE",
                Format = "O3DE asset",
                Application = "O3DE",
                ItemName = type,
                FormatVersion = GameDevDocuments.GetString(root, "SerializationVersion")
                    ?? GameDevDocuments.GetString(root, "version"),
                ScanIncomplete = truncated
            };
        }
    }
}
