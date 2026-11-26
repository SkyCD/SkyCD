using System;
using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Reads Unreal project descriptors (.uproject/.uplugin, JSON5), cooked package headers (.uasset/.umap) and
/// Verse modules. Package counts are cross-checked against the summary size and the source length before use;
/// a header that does not add up publishes <c>Valid=false</c> rather than guessed numbers.
/// </summary>
internal static partial class UnrealReader
{
    private const uint PackageTag = 0x9E2A83C1;
    private const uint CookedFlag = 0x20000000;

    internal static GameDevFacts ReadProject(string text, bool plugin)
    {
        if (!GameDevDocuments.TryParseJson(text, out var document, tolerant: true))
        {
            return Invalid(plugin ? "UPlugin" : "UProject");
        }

        using (document)
        {
            var root = document.RootElement;
            var facts = new GameDevFacts
            {
                Family = "Unreal",
                Format = plugin ? "UPlugin" : "UProject",
                EngineVersion = GameDevDocuments.GetString(root, "EngineAssociation"),
                FormatVersion = GameDevDocuments.GetString(root, "FileVersion"),
                ProjectName = GameDevDocuments.GetString(root, "FriendlyName"),
                Application = "Unreal Engine",
                ResourceCount = GameDevDocuments.CountArray(root, "Modules"),
                ImportCount = GameDevDocuments.CountArray(root, "Plugins"),
                SceneCount = GameDevDocuments.CountArray(root, "TargetPlatforms")
            };

            if (root.TryGetProperty("Modules", out var modules) && modules.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var module in modules.EnumerateArray())
                {
                    facts.AddNameSample(GameDevDocuments.GetString(module, "Name"));
                }
            }

            if (root.TryGetProperty("Plugins", out var plugins) && plugins.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var entry in plugins.EnumerateArray())
                {
                    facts.AddNameSample(GameDevDocuments.GetString(entry, "Name"));
                }
            }

            AddProjectRows(facts, root);
            return facts;
        }
    }

    internal static GameDevFacts ReadVerse(string text, bool truncated)
    {
        var facts = new GameDevFacts
        {
            Family = "Unreal",
            Format = "Verse",
            Application = "Unreal Engine",
            ScanIncomplete = truncated
        };

        long lines = 0;
        long functions = 0;
        long labels = 0;
        foreach (var raw in text.Split('\n'))
        {
            lines++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (FunctionPattern().IsMatch(line))
            {
                functions++;
            }

            if (LabelPattern().IsMatch(line))
            {
                labels++;
            }
        }

        facts.FileCount = lines;
        facts.ScriptCount = functions;
        facts.LabelCount = labels;
        return facts;
    }

    internal static GameDevFacts ReadPackage(byte[] data, bool truncated)
    {
        if (data.Length < 16 || BinaryPrimitives.ReadUInt32LittleEndian(data) != PackageTag)
        {
            return Invalid("UPackage");
        }

        if (!TryReadSummary(data, out var summary))
        {
            return new GameDevFacts
            {
                Family = "Unreal",
                Format = "UPackage",
                Application = "Unreal Engine",
                Valid = false,
                ScanIncomplete = truncated
            };
        }

        var facts = new GameDevFacts
        {
            Family = "Unreal",
            Format = "UPackage",
            Application = "Unreal Engine",
            FormatVersion = $"UE4={summary.FileVersionUE4}; UE5={summary.FileVersionUE5}",
            EngineVersion = summary.FileVersionUE5 > 0
                ? "UE5"
                : summary.FileVersionUE4 > 0
                    ? "UE4"
                    : null,
            ProjectName = summary.PackageName,
            ItemName = summary.PackageName,
            Cooked = summary.Cooked,
            ExportCount = summary.ExportCount,
            ImportCount = summary.ImportCount,
            ResourceCount = summary.NameCount,
            ClassCount = summary.CustomVersionCount,
            HasThumbnail = summary.ThumbnailLength > 0,
            ScanIncomplete = truncated
        };

        return facts;
    }

    /// <summary>Extracts the stored package thumbnail when the table entry validates against the file length.</summary>
    internal static byte[]? TryReadThumbnail(byte[] data)
    {
        if (!TryReadSummary(data, out var summary) || summary.ThumbnailLength <= 0)
        {
            return null;
        }

        var start = summary.ThumbnailOffset;
        var length = summary.ThumbnailLength;
        if (start < 0 || length < 4 || start > data.Length || length > data.Length - start
            || length > GameDevLimits.MaxArtifactBytes)
        {
            return null;
        }

        var isPng = GameDevSource.StartsWith(data.AsSpan(start), 0x89, 0x50, 0x4E, 0x47);
        var isJpeg = GameDevSource.StartsWith(data.AsSpan(start), 0xFF, 0xD8);
        return isPng || isJpeg ? data.AsSpan(start, length).ToArray() : null;
    }

    private static void AddProjectRows(GameDevFacts facts, System.Text.Json.JsonElement root)
    {
        facts.AddPreviewRow("Format: " + facts.Format);
        var association = GameDevDocuments.GetString(root, "EngineAssociation");
        if (!string.IsNullOrWhiteSpace(association))
        {
            facts.AddPreviewRow("Engine: " + association);
        }

        if (root.TryGetProperty("Modules", out var modules) && modules.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var module in modules.EnumerateArray())
            {
                facts.AddPreviewRow("Module: " + GameDevDocuments.GetString(module, "Name"));
            }
        }

        if (root.TryGetProperty("Plugins", out var plugins) && plugins.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var entry in plugins.EnumerateArray())
            {
                facts.AddPreviewRow("Plugin: " + GameDevDocuments.GetString(entry, "Name"));
            }
        }
    }

    private static GameDevFacts Invalid(string format) =>
        new() { Family = "Unreal", Format = format, Application = "Unreal Engine", Valid = false };

    private static bool TryReadSummary(byte[] data, out PackageSummary summary)
    {
        summary = default;
        var offset = 4;
        if (offset + 4 > data.Length)
        {
            return false;
        }

        var legacyVersion = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        int fileVersionUE4 = 0;
        int fileVersionUE5 = 0;

        if (legacyVersion != -4)
        {
            if (offset + 16 > data.Length)
            {
                return false;
            }

            offset += 4; // legacy UE3 version
            fileVersionUE4 = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
            fileVersionUE5 = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4, 4));
            offset += 12;
        }

        if (offset + 4 > data.Length)
        {
            return false;
        }

        var customVersionCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        if (customVersionCount is < 0 or > 512 || offset + (long)customVersionCount * 20 > data.Length)
        {
            return false;
        }

        offset += customVersionCount * 20;

        if (offset + 4 > data.Length)
        {
            return false;
        }

        var totalHeaderSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        if (totalHeaderSize < offset || totalHeaderSize > data.Length)
        {
            return false;
        }

        if (!TryReadString(data, ref offset, out var packageName))
        {
            return false;
        }

        if (offset + 4 > data.Length)
        {
            return false;
        }

        var packageFlags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;

        // NameCount, NameOffset, GatherableTextDataCount, GatherableTextDataOffset,
        // ExportCount, ExportOffset, ImportCount, ImportOffset, DependsOffset,
        // SoftPackageReferencesCount, SoftPackageReferencesOffset, SearchableNamesOffset.
        const int CountsBeforeThumbnail = 12;
        if (offset + (CountsBeforeThumbnail * 4) + 4 > data.Length)
        {
            return false;
        }

        var nameCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        var exportCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 16, 4));
        var importCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 24, 4));
        offset += CountsBeforeThumbnail * 4;
        var thumbnailTableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));

        if (nameCount < 0 || exportCount < 0 || importCount < 0
            || nameCount > 10_000_000 || exportCount > 10_000_000 || importCount > 10_000_000)
        {
            return false;
        }

        summary = new PackageSummary(packageName, fileVersionUE4, fileVersionUE5, customVersionCount, nameCount,
            exportCount, importCount, (packageFlags & CookedFlag) != 0, 0, 0);

        if (thumbnailTableOffset <= 0 || thumbnailTableOffset > data.Length - 4)
        {
            return true;
        }

        var tableOffset = thumbnailTableOffset;
        var entryCount = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, 4));
        tableOffset += 4;
        if (entryCount is < 1 or > 256)
        {
            return true;
        }

        for (var index = 0; index < entryCount; index++)
        {
            if (!TryReadString(data, ref tableOffset, out _)
                || tableOffset + 8 > data.Length)
            {
                return true;
            }

            var thumbnailOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset, 4));
            var thumbnailLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(tableOffset + 4, 4));
            tableOffset += 8;

            if (thumbnailOffset > 0 && thumbnailLength > 0
                && thumbnailOffset <= data.Length && thumbnailLength <= data.Length - thumbnailOffset)
            {
                summary = summary with { ThumbnailOffset = thumbnailOffset, ThumbnailLength = thumbnailLength };
                return true;
            }
        }

        return true;
    }

    private static bool TryReadString(byte[] data, ref int offset, out string value)
    {
        value = string.Empty;
        if (offset + 4 > data.Length)
        {
            return false;
        }

        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;

        if (length == 0)
        {
            return true;
        }

        if (length > 0)
        {
            if (length > 4096 || offset + length > data.Length)
            {
                return false;
            }

            var bytes = data.AsSpan(offset, length);
            if (bytes[^1] == 0)
            {
                bytes = bytes[..^1];
            }

            value = Encoding.UTF8.GetString(bytes);
            offset += length;
            return true;
        }

        var wideLength = -length;
        if (wideLength > 4096 || offset + (long)wideLength * 2 > data.Length)
        {
            return false;
        }

        var wide = data.AsSpan(offset, wideLength * 2);
        if (wide.Length >= 2 && wide[^1] == 0 && wide[^2] == 0)
        {
            wide = wide[..^2];
        }

        value = Encoding.Unicode.GetString(wide);
        offset += wideLength * 2;
        return true;
    }

    [GeneratedRegex(@"^\s*\w+\s*\([^)]*\)\s*(?::|=)", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionPattern();

    [GeneratedRegex(@"^\s*\w+\s*:=\s*class\b", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();

    private readonly record struct PackageSummary(
        string PackageName,
        int FileVersionUE4,
        int FileVersionUE5,
        int CustomVersionCount,
        int NameCount,
        int ExportCount,
        int ImportCount,
        bool Cooked,
        int ThumbnailOffset,
        int ThumbnailLength);
}
