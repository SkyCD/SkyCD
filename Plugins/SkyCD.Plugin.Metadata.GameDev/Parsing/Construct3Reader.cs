using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using SkyCD.Plugin.Metadata.GameDev.Exceptions;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Construct 3 <c>.c3p</c> archives from their central directory. Only <c>project.c3proj</c> is opened,
/// under a fixed bound; no other entry is extracted.
/// </summary>
internal static class Construct3Reader
{
    internal static GameDevFacts? Read(IMediaSource source, CancellationToken cancellationToken)
    {
        try
        {
            using var archive = GameDevSource.OpenZip(source, cancellationToken);
            var facts = new GameDevFacts
            {
                Family = "Construct",
                Format = "Construct 3 project",
                Application = "Construct 3"
            };

            var entries = 0;
            ZipArchiveEntry? project = null;
            foreach (var entry in archive.Entries)
            {
                if (++entries > GameDevLimits.MaxArchiveEntries)
                {
                    facts.ScanIncomplete = true;
                    break;
                }

                if (string.Equals(entry.FullName, "project.c3proj", StringComparison.OrdinalIgnoreCase))
                {
                    project = entry;
                }
            }

            facts.FileCount = entries;
            facts.AssetCount = entries;
            if (project is null)
            {
                facts.Valid = false;
                return facts;
            }

            var text = GameDevSource.DecodeText(GameDevSource.ReadEntry(project, 1024 * 1024));
            if (!GameDevDocuments.TryParseJson(text, out var document))
            {
                facts.Valid = false;
                return facts;
            }

            using (document)
            {
                var root = document.RootElement;
                facts.FormatVersion = GameDevDocuments.GetString(root, "projectFormatVersion");
                facts.EngineVersion = GameDevDocuments.GetString(root, "savedWithRelease");
                facts.SceneCount = GameDevDocuments.CountArray(root, "layouts");
                facts.BlockCount = GameDevDocuments.CountArray(root, "eventSheets");
                facts.ScriptCount = GameDevDocuments.CountArray(root, "scripts");

                if (root.TryGetProperty("layouts", out var layouts)
                    && layouts.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var layout in layouts.EnumerateArray())
                    {
                        var name = GameDevDocuments.GetString(layout, "name");
                        facts.AddNameSample(name);
                        facts.AddPreviewRow("Layout: " + name);
                    }
                }
            }

            return facts;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or GameDevLimitExceededException or NotSupportedException)
        {
            return null;
        }
    }
}
