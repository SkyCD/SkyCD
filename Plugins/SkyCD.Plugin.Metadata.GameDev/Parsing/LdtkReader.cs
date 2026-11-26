using System;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads LDtk project and level documents from their versioned JSON schema.</summary>
internal static class LdtkReader
{
    internal static GameDevFacts? Read(string text, bool level, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "LDtk",
                Format = level ? "LDtk level" : "LDtk project",
                Application = "LDtk",
                FormatVersion = GameDevDocuments.GetString(root, "jsonVersion"),
                EngineVersion = GameDevDocuments.GetString(root, "appVersion"),
                ProjectName = GameDevDocuments.GetString(root, "name"),
                ScanIncomplete = truncated
            };

            facts.LevelCount = 1;
            if (!level && GameDevDocuments.CountArray(root, "levels") is { } levels)
            {
                facts.LevelCount = levels;
                if (root.TryGetProperty("levels", out var array)
                    && array.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var entry in array.EnumerateArray())
                    {
                        facts.AddPreviewRow("Level: " + GameDevDocuments.GetString(entry, "identifier"));
                        facts.AddNameSample(GameDevDocuments.GetString(entry, "identifier"));
                    }
                }
            }

            facts.SceneCount = GameDevDocuments.CountArray(root, "worlds");
            if (root.TryGetProperty("defs", out var definitions) && definitions.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                facts.ClassCount = GameDevDocuments.CountArray(definitions, "entities");
                facts.ResourceCount = GameDevDocuments.CountArray(definitions, "tilesets");
            }

            if (root.TryGetProperty("layerInstances", out var instances)
                && instances.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                facts.LayerCount = instances.GetArrayLength();
            }

            facts.AddPreviewRow("Format: " + facts.Format);
            return facts;
        }
    }
}
