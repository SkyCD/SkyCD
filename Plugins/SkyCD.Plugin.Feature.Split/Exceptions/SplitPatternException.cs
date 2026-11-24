using System;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

internal sealed class SplitPatternException(string pattern, string reason)
    : ArgumentException($"The part file naming pattern '{pattern}' is not valid: {reason}.", nameof(pattern));
