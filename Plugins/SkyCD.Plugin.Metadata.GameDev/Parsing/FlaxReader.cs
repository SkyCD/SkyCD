using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads a Flax project descriptor. Scene, prefab and material JSON wait for the shared probe.</summary>
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
}
