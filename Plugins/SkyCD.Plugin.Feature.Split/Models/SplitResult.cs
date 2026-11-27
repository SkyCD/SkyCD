using System.Collections.Generic;

namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>The outcome of a split run.</summary>
/// <param name="Parts">The part files in write order.</param>
/// <param name="TotalEntries">The entries written across all parts.</param>
public sealed record SplitResult(IReadOnlyList<SplitPart> Parts, int TotalEntries);
