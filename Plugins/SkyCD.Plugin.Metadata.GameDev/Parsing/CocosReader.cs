using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads Cocos Creator flat JSON scenes and Cocos Studio XML scenes.</summary>
internal static class CocosReader
{
    internal static GameDevFacts? ReadCreator(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("__type__", out _))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "Cocos",
                Format = GameDevDocuments.GetString(root, "__type__") ?? "Cocos Creator asset",
                Application = "Cocos Creator",
                ItemName = GameDevDocuments.GetString(root, "_name"),
                ScanIncomplete = truncated
            };

            var types = new HashSet<string>(StringComparer.Ordinal);
            var names = 0;
            CountNodes(root, facts, types, ref names, 0);
            facts.ClassCount = types.Count;
            return facts;
        }
    }

    internal static GameDevFacts? ReadStudio(string text, bool truncated)
    {
        try
        {
            using var reader = GameDevDocuments.CreateXml(text);
            if (!GameDevDocuments.ReadRootElement(reader))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "Cocos",
                Format = "Cocos Studio scene",
                Application = "Cocos Studio",
                FormatVersion = reader.GetAttribute("Version") ?? reader.GetAttribute("version"),
                ItemName = reader.GetAttribute("Name"),
                ScanIncomplete = truncated
            };

            var depth = reader.Depth;
            long nodes = 0;
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

                var name = reader.GetAttribute("Name");
                if (name is not null)
                {
                    nodes++;
                    facts.AddNameSample(name);
                    facts.AddPreviewRow("Node: " + name);
                }
            }

            facts.NodeCount = nodes;
            return facts;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static void CountNodes(JsonElement element, GameDevFacts facts, HashSet<string> types,
        ref int names, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("__type__", out var type) && type.ValueKind == JsonValueKind.String)
                {
                    var value = type.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        types.Add(value);
                    }
                }

                if (names < GameDevLimits.MaxListEntryCount
                    && element.TryGetProperty("_name", out var name)
                    && name.ValueKind == JsonValueKind.String)
                {
                    var text = name.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        facts.AddNameSample(text);
                        facts.AddPreviewRow("Node: " + text);
                        names++;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    CountNodes(property.Value, facts, types, ref names, depth + 1);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CountNodes(item, facts, types, ref names, depth + 1);
                }

                break;
        }
    }
}
