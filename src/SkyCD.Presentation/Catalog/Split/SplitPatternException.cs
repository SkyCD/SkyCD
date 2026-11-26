using System;

namespace SkyCD.Presentation.Catalog.Split;

/// <summary>The part file naming pattern is not a plain file name in the output folder.</summary>
public sealed class SplitPatternException(string pattern, string reason)
    : ArgumentException($"The part file naming pattern '{pattern}' is not valid: {reason}.", nameof(pattern));
