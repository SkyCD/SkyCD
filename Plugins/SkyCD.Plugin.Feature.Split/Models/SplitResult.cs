using System.Collections.Generic;

namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>The outcome of a completed split run.</summary>
public sealed record SplitResult(IReadOnlyList<SplitPart> Parts, int TotalEntries);
