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
    internal static GameDevFacts? ReadCreatorAsset(string extension, string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!HasCreatorType(root))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "Cocos",
                Format = FormatName(extension),
                Application = "Cocos Creator",
                ItemName = GameDevDocuments.GetString(root, "_name"),
                ScanIncomplete = truncated
            };

            var types = new HashSet<string>(StringComparer.Ordinal);
            var names = 0;
            CountNodes(root, facts, types, ref names, 0);
            facts.ClassCount = types.Count;
            if (facts.ItemName is null && facts.NameSamples.Count > 0)
            {
                facts.ItemName = facts.NameSamples[0];
            }

            facts.AddPreviewRow("Format: " + facts.Format);
            return facts;
        }
    }

    internal static GameDevFacts? ReadMeta(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!HasProperty(root, "uuid") || !HasProperty(root, "importer"))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "Cocos",
                Format = "Cocos meta",
                Application = "Cocos Creator",
                FormatVersion = GameDevDocuments.GetString(root, "ver"),
                MetaGuid = GameDevDocuments.GetString(root, "uuid"),
                ImporterType = GameDevDocuments.GetString(root, "importer"),
                FileCount = GameDevDocuments.CountArray(root, "files"),
                ScanIncomplete = truncated
            };

            if (root.TryGetProperty("subMetas", out var subMetas) && subMetas.ValueKind == JsonValueKind.Object)
            {
                var count = 0;
                foreach (var _ in subMetas.EnumerateObject())
                {
                    count++;
                }

                facts.ResourceCount = count;
            }

            facts.AddPreviewRow("Importer: " + (facts.ImporterType ?? "unknown"));
            return facts;
        }
    }

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

    private static bool HasProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out _);

    /// <summary>Cocos Creator 2.x stores an object root; 3.x scenes and prefabs are arrays of typed objects.</summary>
    private static bool HasCreatorType(JsonElement root)
    {
        if (HasProperty(root, "__type__"))
        {
            return true;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in root.EnumerateArray())
        {
            if (HasProperty(item, "__type__"))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatName(string extension) => extension.ToLowerInvariant() switch
    {
        ".scene" => "Cocos scene",
        ".prefab" => "Cocos prefab",
        ".anim" => "Cocos animation",
        ".mtl" => "Cocos material",
        _ => "Cocos Creator asset"
    };

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
