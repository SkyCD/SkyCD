using System;
using System.Collections.Generic;
using SkyCD.Plugin.Metadata.Archives.Arc.Models;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Parsing;

/// <summary>Counts structural problems found while listing an archive so a result can report them once.</summary>
internal sealed class ArcDiagnostics
{
    private readonly Dictionary<string, int> counts = new(StringComparer.Ordinal);

    /// <summary>Gets whether no problem was recorded.</summary>
    public bool IsEmpty => counts.Count == 0;

    /// <summary>Gets the recorded codes, in first-seen order.</summary>
    public IReadOnlyCollection<string> Codes => counts.Keys;

    /// <summary>Records one occurrence of a published problem code.</summary>
    public void Add(string code)
    {
        DiagnosticCodes.Validate(code);
        counts[code] = TryGetCount(code) + 1;
    }

    /// <summary>Gets how many entries recorded the given code.</summary>
    public int Count(string code) => TryGetCount(code);

    private int TryGetCount(string code) => counts.TryGetValue(code, out var count) ? count : 0;
}
