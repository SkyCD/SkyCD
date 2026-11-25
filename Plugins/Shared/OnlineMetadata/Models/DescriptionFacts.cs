using System;
using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.OnlineMetadata.Models;

/// <summary>
/// Named string facts collected from online sources and the local entry, keyed case-insensitively for the
/// template engine. Earlier-imported sources win a key so a preferred source is never overwritten, and the
/// distinct source URLs are tracked for provenance.
/// </summary>
public sealed class DescriptionFacts
{
    private const int MaxValueLength = 4000;
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> sourceUrls = [];

    /// <summary>Defined fact keys, sorted for deterministic template rendering and diagnostics.</summary>
    public IReadOnlyList<string> Keys => values.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Distinct source URLs backing the facts, in the order they were first recorded.</summary>
    public IReadOnlyList<string> SourceUrls => sourceUrls;

    /// <summary>True when a key holds a non-whitespace value.</summary>
    public bool Has(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    /// <summary>Reads a value, or the empty string when absent.</summary>
    public string Get(string key) => values.TryGetValue(key, out var value) ? value : string.Empty;

    /// <summary>Stores a value unless an equal-or-higher priority source already set a non-empty one.</summary>
    public void Set(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var normalized = Sanitize(value);
        if (string.IsNullOrEmpty(normalized)) return;
        if (!values.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing))
            values[key] = normalized;
    }

    /// <summary>Forces a value, overwriting any earlier one. Used for locally derived facts that must win.</summary>
    public void Force(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        var normalized = Sanitize(value);
        if (!string.IsNullOrEmpty(normalized)) values[key] = normalized;
    }

    /// <summary>Records a backing source URL once; the value is used verbatim in provenance.</summary>
    public void AddSourceUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!sourceUrls.Contains(url, StringComparer.OrdinalIgnoreCase)) sourceUrls.Add(url);
    }

    /// <summary>Copies the facts into a by-value dictionary a source capability can return.</summary>
    public IReadOnlyDictionary<string, string> Snapshot()
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in values.Keys) copy[key] = values[key];
        return copy;
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        return trimmed.Length > MaxValueLength ? trimmed[..MaxValueLength] : trimmed;
    }
}
