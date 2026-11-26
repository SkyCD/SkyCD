using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads an s&amp;box project descriptor. Scenes and prefabs wait for the shared probe.</summary>
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
}
