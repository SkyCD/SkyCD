using System;

namespace SkyCD.Plugin.Metadata.YamlToml.Models;

/// <summary>Bounds the YAML/TOML plugin applies on top of the host's own limits.</summary>
public sealed record YamlTomlPluginOptions
{
    public YamlTomlPluginOptions(
        long maxSourceBytes = 16 * 1024 * 1024,
        int maxMetadataValueCharacters = 2048,
        int maxTopLevelNames = 32,
        int maxThumbnailDimension = 768,
        int maxRenderedCharacters = 6000,
        int maxRenderedLines = 200)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataValueCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTopLevelNames);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxThumbnailDimension);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRenderedLines);

        MaxSourceBytes = maxSourceBytes;
        MaxMetadataValueCharacters = maxMetadataValueCharacters;
        MaxTopLevelNames = maxTopLevelNames;
        MaxThumbnailDimension = maxThumbnailDimension;
        MaxRenderedCharacters = maxRenderedCharacters;
        MaxRenderedLines = maxRenderedLines;
    }

    /// <summary>Sources larger than this are rejected before any byte is read.</summary>
    public long MaxSourceBytes { get; }

    /// <summary>Upper bound applied to each published text value.</summary>
    public int MaxMetadataValueCharacters { get; }

    /// <summary>Upper bound of the top-level key or table names published as one list.</summary>
    public int MaxTopLevelNames { get; }

    /// <summary>Page width in pixels of the rendered thumbnail.</summary>
    public int MaxThumbnailDimension { get; }

    /// <summary>Upper bound of the characters drawn on the rendered page.</summary>
    public int MaxRenderedCharacters { get; }

    /// <summary>Upper bound of the lines drawn on the rendered page.</summary>
    public int MaxRenderedLines { get; }

    public static YamlTomlPluginOptions Default { get; } = new();
}
