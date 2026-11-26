using System;
using System.Collections.Generic;
using System.Text.Json;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads s&amp;box project descriptors and the scene and prefab JSON that shares its extensions with other
/// engines. Objects carry <c>__class</c>; serialized game objects also carry <c>__guid</c>.
/// </summary>
internal static class SandboxReader
{
    internal static GameDevFacts? Read(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document, tolerant: true))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var facts = new GameDevFacts
            {
                Family = "s&box",
                Format = "s&box project",
                Application = "s&box",
                ProjectName = GameDevDocuments.GetString(root, "Title")
                    ?? GameDevDocuments.GetString(root, "Name"),
                FormatVersion = GameDevDocuments.GetString(root, "Type"),
                ItemName = GameDevDocuments.GetString(root, "StartupScene"),
                ScanIncomplete = truncated
            };

            facts.ResourceCount = GameDevDocuments.CountArray(root, "Resources");
            facts.ScriptCount = GameDevDocuments.CountArray(root, "Code");
            facts.AddPreviewRow("Format: s&box project");
            facts.AddPreviewRow("Startup scene: " + facts.ItemName);
            return facts;
        }
    }

    internal static GameDevFacts? ReadAsset(string extension, string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document, tolerant: true))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("__class", out _)
                || !root.TryGetProperty("__guid", out _))
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "s&box",
                Format = FormatName(extension),
                Application = "s&box",
                ItemName = GameDevDocuments.GetString(root, "__class"),
                ScanIncomplete = truncated
            };

            var classes = new HashSet<string>(StringComparer.Ordinal);
            long entities = 0;
            long components = 0;
            Walk(root, facts, classes, ref entities, ref components, 0);
            facts.EntityCount = entities;
            facts.ObjectCount = components;
            facts.ClassCount = classes.Count;
            facts.AddPreviewRow("Format: " + facts.Format);
            return facts;
        }
    }

    private static void Walk(JsonElement element, GameDevFacts facts, HashSet<string> classes,
        ref long entities, ref long components, int depth)
    {
        if (depth > 64)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (GameDevDocuments.GetString(element, "__class") is { Length: > 0 } className)
                {
                    classes.Add(className);
                    if (element.TryGetProperty("__guid", out _))
                    {
                        entities++;
                    }
                    else
                    {
                        components++;
                    }
                }

                if (GameDevDocuments.GetString(element, "Name") is { Length: > 0 } name)
                {
                    facts.AddNameSample(name);
                }

                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, facts, classes, ref entities, ref components, depth + 1);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, facts, classes, ref entities, ref components, depth + 1);
                }

                break;
        }
    }

    private static string FormatName(string extension) => extension.ToLowerInvariant() switch
    {
        ".scene" => "s&box scene",
        ".prefab" => "s&box prefab",
        _ => "s&box asset"
    };
}
