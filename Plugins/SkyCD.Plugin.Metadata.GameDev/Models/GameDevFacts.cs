using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.GameDev.Models;

/// <summary>
/// Family-neutral container for facts collected by one engine reader. Absent values stay <c>null</c> so the
/// projection can omit them, and a recognized but damaged file sets <see cref="Valid"/> to <c>false</c> without
/// contributing guessed numbers.
/// </summary>
internal sealed class GameDevFacts
{
    public string Family { get; set; } = string.Empty;

    public string Format { get; set; } = string.Empty;

    public string? FormatVersion { get; set; }

    public string? EngineVersion { get; set; }

    public string? Application { get; set; }

    public string? ProjectName { get; set; }

    public string? ItemName { get; set; }

    public bool? Compiled { get; set; }

    public bool? Cooked { get; set; }

    public bool? Valid { get; set; }

    public bool ScanIncomplete { get; set; }

    public long? NodeCount { get; set; }

    public long? EntityCount { get; set; }

    public long? InstanceCount { get; set; }

    public long? ObjectCount { get; set; }

    public long? SceneCount { get; set; }

    public long? LevelCount { get; set; }

    public long? LayerCount { get; set; }

    public long? TileLayerCount { get; set; }

    public long? ObjectGroupCount { get; set; }

    public long? FrameCount { get; set; }

    public long? TagCount { get; set; }

    public long? AssetCount { get; set; }

    public long? ResourceCount { get; set; }

    public long? ImportCount { get; set; }

    public long? ExportCount { get; set; }

    public long? FileCount { get; set; }

    public long? ScriptCount { get; set; }

    public long? LabelCount { get; set; }

    public long? CharacterCount { get; set; }

    public long? TrackCount { get; set; }

    public long? ClipCount { get; set; }

    public long? ChunkCount { get; set; }

    public long? BlockCount { get; set; }

    public long? ClassCount { get; set; }

    public long? ImageWidth { get; set; }

    public long? ImageHeight { get; set; }

    public long? MapWidth { get; set; }

    public long? MapHeight { get; set; }

    public long? TileWidth { get; set; }

    public long? TileHeight { get; set; }

    public string? ColorMode { get; set; }

    public long? ColorDepth { get; set; }

    public long? TotalDurationMilliseconds { get; set; }

    public bool? Loop { get; set; }

    public bool HasThumbnail { get; set; }

    /// <summary>Bounded animation or tag names, joined when published.</summary>
    public List<string> AnimationNames { get; } = [];

    /// <summary>Bounded names for objects, entities, classes or regions.</summary>
    public List<string> NameSamples { get; } = [];

    /// <summary>Bounded tileset names.</summary>
    public List<string> TilesetNames { get; } = [];

    /// <summary>Bounded rows for the structure page; empty when the family has no structure page.</summary>
    public List<string> PreviewRows { get; } = [];

    public void AddAnimationName(string? name) => AddBounded(AnimationNames, name);

    public void AddNameSample(string? name) => AddBounded(NameSamples, name);

    public void AddTilesetName(string? name) => AddBounded(TilesetNames, name);

    public void AddPreviewRow(string? row) => AddBounded(PreviewRows, row, GameDevLimits.MaxPreviewRows);

    private static void AddBounded(List<string> target, string? value, int limit = GameDevLimits.MaxListEntryCount)
    {
        if (target.Count >= limit || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > GameDevLimits.MaxListEntryLength)
        {
            trimmed = trimmed[..GameDevLimits.MaxListEntryLength];
        }

        if (!target.Exists(entry => string.Equals(entry, trimmed, System.StringComparison.Ordinal)))
        {
            target.Add(trimmed);
        }
    }
}

/// <summary>Bounds every reader applies on top of the host's own limits.</summary>
internal static class GameDevLimits
{
    /// <summary>Maximum text or header bytes read for one provider run.</summary>
    internal const int MaxTextBytes = 4 * 1024 * 1024;

    /// <summary>Maximum bytes buffered for tools that need the whole document (Aseprite, ZIP previews).</summary>
    internal const int MaxWholeSourceBytes = 16 * 1024 * 1024;

    /// <summary>Maximum decompressed bytes taken from one archive entry.</summary>
    internal const int MaxEntryBytes = 8 * 1024 * 1024;

    /// <summary>Maximum total decompressed bytes across all archive entries.</summary>
    internal const int MaxArchiveBytes = 8 * 1024 * 1024;

    /// <summary>Maximum entries inspected in a ZIP or tar container.</summary>
    internal const int MaxArchiveEntries = 4096;

    /// <summary>Entries per published list field.</summary>
    internal const int MaxListEntryCount = 20;

    /// <summary>Characters per published list entry.</summary>
    internal const int MaxListEntryLength = 120;

    /// <summary>Rows drawn on one structure page.</summary>
    internal const int MaxPreviewRows = 64;

    /// <summary>Longest edge of a generated thumbnail.</summary>
    internal const int MaxThumbnailDimension = 512;

    /// <summary>Largest artifact this plugin produces.</summary>
    internal const int MaxArtifactBytes = 4 * 1024 * 1024;
}
