using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads O3DE slice XML, asset JSON, and the prefab and material JSON that share their extensions with other
/// engines. Entity and component names come from the document; referenced assets are never opened.
/// </summary>
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

    internal static GameDevFacts? ReadPrefab(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ContainerEntity", out var container))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "O3DE",
                Format = "O3DE prefab",
                Application = "O3DE",
                ItemName = GameDevDocuments.GetString(container, "Name"),
                ScanIncomplete = truncated
            };

            var classes = new HashSet<string>(StringComparer.Ordinal);
            long entities = 0;
            long components = 0;
            WalkPrefab(root, facts, classes, ref entities, ref components, 0);
            facts.EntityCount = entities;
            facts.ObjectCount = components;
            facts.ClassCount = classes.Count;
            facts.InstanceCount = GameDevDocuments.CountArray(root, "Instances");
            facts.AddPreviewRow("Format: O3DE prefab");
            return facts;
        }
    }

    internal static GameDevFacts? ReadMaterial(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var materialType = GameDevDocuments.GetString(root, "materialType");
            if (string.IsNullOrWhiteSpace(materialType))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "O3DE",
                Format = "O3DE material",
                Application = "O3DE",
                ItemName = LastSegment(materialType),
                FormatVersion = GameDevDocuments.GetString(root, "materialTypeVersion"),
                ObjectCount = CountProperties(root, "propertyValues"),
                ScanIncomplete = truncated
            };

            if (root.TryGetProperty("propertyValues", out var groups)
                && groups.ValueKind == JsonValueKind.Object)
            {
                foreach (var group in groups.EnumerateObject())
                {
                    facts.AddNameSample(group.Name);
                    facts.AddPreviewRow("Group: " + group.Name);
                }
            }

            facts.AddPreviewRow("Type: " + facts.ItemName);
            return facts;
        }
    }

    /// <summary>A prefab nests its root, entities, component objects and child prefabs; all are walked once.</summary>
    private static void WalkPrefab(JsonElement element, GameDevFacts facts, HashSet<string> classes,
        ref long entities, ref long components, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (GameDevDocuments.GetString(element, "Id") is not null
                    && GameDevDocuments.GetString(element, "Name") is { } entityName)
                {
                    entities++;
                    facts.AddNameSample(entityName);
                }

                if (GameDevDocuments.GetString(element, "$type") is { Length: > 0 } type)
                {
                    components++;
                    classes.Add(type);
                }

                foreach (var property in element.EnumerateObject())
                {
                    WalkPrefab(property.Value, facts, classes, ref entities, ref components, depth + 1);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    WalkPrefab(item, facts, classes, ref entities, ref components, depth + 1);
                }

                break;
        }
    }

    private static long? CountProperties(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        long count = 0;
        foreach (var _ in value.EnumerateObject())
        {
            count++;
        }

        return count;
    }

    private static string LastSegment(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash >= 0 && slash + 1 < path.Length ? path[(slash + 1)..] : path;
    }
}
