using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.MediaMetadata;

/// <summary>
/// Accumulates what the carriers found. Readers run from the most explicit source to the least, so the
/// comment and rating keep the first value they see; keywords are merged and deduplicated
/// case-insensitively. Every entry point is defensive: damaged input contributes nothing instead of failing
/// the extraction.
/// </summary>
internal sealed class MetadataScan
{
    public const int MaxCommentLength = 2000;
    public const int MaxTagLength = 128;
    public const int MaxTagCount = 64;

    private readonly List<string> keywords = [];
    private readonly HashSet<string> seenKeywords = new(StringComparer.OrdinalIgnoreCase);

    public string? Comment { get; private set; }

    public int? Rating { get; private set; }

    public IReadOnlyList<string> Keywords => keywords;

    /// <summary>Adds a single keyword; empty, oversized and duplicate values are dropped.</summary>
    public void AddKeyword(string? value)
    {
        var normalized = MetadataText.NormalizeKeyword(value ?? string.Empty);
        if (normalized.Length == 0 || normalized.Length > MaxTagLength)
        {
            return;
        }

        if (keywords.Count < MaxTagCount && seenKeywords.Add(normalized))
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

        foreach (var candidate in value.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries
                     | StringSplitOptions.TrimEntries))
        {
            AddKeyword(candidate);
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
            Comment = MetadataText.Cap(normalized, MaxCommentLength);
        }
    }

    /// <summary>
    /// Sets a rating only when none was found yet. Values are normalized to 0–5 stars from the 1–5 and 0–99
    /// scales tools persist; zero, negative ("rejected") and absent values produce no rating.
    /// </summary>
    public void SetRating(int? value)
    {
        if (Rating is not null || value is null)
        {
            return;
        }

        Rating = NormalizeRating(value.Value);
    }

    /// <summary>
    /// Maps the Windows percentage scale to stars using the Shell's own ranges, so the 25 a user picked in
    /// Explorer becomes two stars rather than one. Values of 1–5 are already stars.
    /// </summary>
    public static int? NormalizeRating(int value) => value switch
    {
        <= 0 => null,
        <= 5 => value,
        <= 12 => 1,
        <= 37 => 2,
        <= 62 => 3,
        <= 87 => 4,
        _ => 5
    };
}
