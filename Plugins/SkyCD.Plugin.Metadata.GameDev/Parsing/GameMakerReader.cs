using System;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Metadata.GameDev.Exceptions;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads GameMaker project and resource descriptors, and the entry count of a project archive.</summary>
internal static class GameMakerReader
{
    internal static GameDevFacts? ReadProject(string text, bool truncated)
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
                Family = "GameMaker",
                Format = "GameMaker project",
                Application = "GameMaker",
                ProjectName = GameDevDocuments.GetString(root, "name"),
                EngineVersion = GameDevDocuments.GetString(root, "IDEVersion"),
                FormatVersion = GameDevDocuments.GetString(root, "resourceVersion"),
                ResourceCount = GameDevDocuments.CountArray(root, "resources"),
                SceneCount = GameDevDocuments.CountArray(root, "RoomOrderNodes"),
                ImportCount = GameDevDocuments.CountArray(root, "Folders"),
                ScanIncomplete = truncated
            };

            facts.BlockCount = GameDevDocuments.CountArray(root, "AudioGroups");
            facts.ChunkCount = GameDevDocuments.CountArray(root, "TextureGroups");
            if (root.TryGetProperty("resources", out var resources)
                && resources.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var resource in resources.EnumerateArray())
                {
                    facts.AddPreviewRow("Resource: " + GameDevDocuments.GetString(resource, "id"));
                }
            }

            return facts;
        }
    }

    internal static GameDevFacts? ReadResource(string text, bool truncated)
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
                Family = "GameMaker",
                Format = GameDevDocuments.GetString(root, "resourceType") ?? "GameMaker resource",
                Application = "GameMaker",
                ItemName = GameDevDocuments.GetString(root, "name"),
                FormatVersion = GameDevDocuments.GetString(root, "resourceVersion"),
                ScanIncomplete = truncated
            };

            if (root.TryGetProperty("parent", out var parent))
            {
                facts.AddNameSample(GameDevDocuments.GetString(parent, "name"));
            }

            return facts;
        }
    }

    internal static GameDevFacts? ReadArchive(IMediaSource source, CancellationToken cancellationToken,
        out byte[]? projectBytes)
    {
        projectBytes = null;
        try
        {
            using var archive = GameDevSource.OpenZip(source, cancellationToken);
            var facts = new GameDevFacts
            {
                Family = "GameMaker",
                Format = "GameMaker project archive",
                Application = "GameMaker"
            };

            var entries = 0;
            foreach (var entry in archive.Entries)
            {
                if (++entries > GameDevLimits.MaxArchiveEntries)
                {
                    facts.ScanIncomplete = true;
                    break;
                }

                if (entry.FullName.EndsWith(".yyp", StringComparison.OrdinalIgnoreCase))
                {
                    projectBytes = GameDevSource.ReadEntry(entry, 1024 * 1024);
                }
            }

            facts.FileCount = entries;
            facts.AssetCount = entries;
            return facts;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or GameDevLimitExceededException or NotSupportedException)
        {
            return null;
        }
    }
}
