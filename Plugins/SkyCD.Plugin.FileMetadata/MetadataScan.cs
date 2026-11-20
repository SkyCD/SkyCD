using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Accumulates what the container readers found. Readers run from the most explicit source to the least, so
/// comment and rating keep the first value they see; keywords are merged and deduplicated case-insensitively.
/// Every entry point is defensive — damaged input contributes nothing instead of failing the extraction.
/// </summary>
internal sealed class MetadataScan
{
    private readonly List<string> keywords = [];
    private readonly List<string> hierarchicalPaths = [];
    private readonly HashSet<string> seenKeywords = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> seenHierarchical = new(StringComparer.OrdinalIgnoreCase);

    public string? Comment { get; private set; }

    public int? Rating { get; private set; }

    public IReadOnlyList<string> Keywords => keywords;

    /// <summary>Hierarchical paths exactly as stored, for example <c>Places|Beach|Sunset</c>.</summary>
    public IReadOnlyList<string> HierarchicalPaths => hierarchicalPaths;

    /// <summary>Adds a single flat keyword; empty, oversized and duplicate values are dropped.</summary>
    public void AddKeyword(string? value)
    {
        var normalized = MetadataText.NormalizeKeyword(value ?? string.Empty);
        if (normalized.Length == 0 || normalized.Length > FileMetadataSchema.MaxTagLength)
        {
            return;
        }

        if (keywords.Count < FileMetadataSchema.MaxTagCount && seenKeywords.Add(normalized))
        {
            keywords.Add(normalized);
        }
    }

    /// <summary>Adds a keyword list that a tool persisted as one delimited string.</summary>
    public void AddKeywords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var candidate in value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            AddKeyword(candidate);
        }
    }

    /// <summary>Adds one hierarchical path; empty and duplicate paths are dropped.</summary>
    public void AddHierarchical(string? value)
    {
        var normalized = MetadataText.NormalizeKeyword(value ?? string.Empty);
        if (normalized.Length == 0 || normalized.Length > FileMetadataSchema.MaxTagLength)
        {
            return;
        }

        if (hierarchicalPaths.Count < FileMetadataSchema.MaxTagCount && seenHierarchical.Add(normalized))
        {
            hierarchicalPaths.Add(normalized);
        }
    }

    /// <summary>Adds a comment only when none was found yet, so embedded XMP wins over container COM text.</summary>
    public void SetComment(string? value)
    {
        if (Comment is not null)
        {
            return;
        }

        var normalized = MetadataText.Clean(value);
        if (normalized.Length > 0)
        {
            Comment = MetadataText.Cap(normalized, FileMetadataSchema.MaxCommentLength);
        }
    }

    /// <summary>
    /// Sets a rating only when none was found yet. Percent-style values (1–99, as Windows persists them) are
    /// converted to stars; negative values mean "rejected" in XMP and are ignored.
    /// </summary>
    public void SetRating(int? value)
    {
        if (Rating is not null || value is null)
        {
            return;
        }

        Rating = NormalizeRating(value.Value);
    }

    public static int? NormalizeRating(int value)
    {
        if (value < 0)
        {
            return null;
        }

        if (value <= 5)
        {
            return value;
        }

        if (value > 99)
        {
            return 5;
        }

        return Math.Clamp((int)Math.Round(value / 20d, MidpointRounding.AwayFromZero), 1, 5);
    }
}
