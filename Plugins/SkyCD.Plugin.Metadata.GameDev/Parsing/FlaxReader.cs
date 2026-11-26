using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Flax project descriptors and the scene, prefab and material JSON that shares its extensions with other
/// engines. Assets are typed by <c>TypeName</c> and written by a named <c>EngineBuild</c>.
/// </summary>
internal static class FlaxReader
{
    internal static GameDevFacts? Read(string text, bool truncated)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document))
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var facts = new GameDevFacts
            {
                Family = "Flax",
                Format = "Flax project",
                Application = "Flax Engine",
                ProjectName = GameDevDocuments.GetString(root, "Name") ?? GameDevDocuments.GetString(root, "ProjectName"),
                EngineVersion = GameDevDocuments.GetString(root, "Version"),
                FormatVersion = GameDevDocuments.GetString(root, "GameTarget"),
                ItemName = GameDevDocuments.GetString(root, "DefaultScene"),
                ScanIncomplete = truncated
            };

            facts.AddPreviewRow("Format: Flax project");
            facts.AddPreviewRow("Game target: " + facts.FormatVersion);
            facts.AddPreviewRow("Default scene: " + facts.ItemName);
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
            var typeName = GameDevDocuments.GetString(root, "TypeName");
            var engineBuild = GameDevDocuments.GetString(root, "EngineBuild");
            if (string.IsNullOrWhiteSpace(typeName) || engineBuild is null)
            {
                return null;
            }

            var facts = new GameDevFacts
            {
                Family = "Flax",
                Format = FormatName(extension),
                Application = "Flax Engine",
                ItemName = typeName,
                FormatVersion = engineBuild,
                NodeCount = GameDevSource.CountOccurrences(text, "\"ID\""),
                ScanIncomplete = truncated
            };

            facts.AddPreviewRow("Format: " + facts.Format);
            facts.AddPreviewRow("Type: " + typeName);
            if (GameDevDocuments.GetString(root, "ID") is { Length: > 0 } id)
            {
                facts.AddPreviewRow("Id: " + id);
            }

            return facts;
        }
    }

    private static string FormatName(string extension) => extension.ToLowerInvariant() switch
    {
        ".scene" => "Flax scene",
        ".prefab" => "Flax prefab",
        ".material" => "Flax material",
        _ => "Flax asset"
    };
}
