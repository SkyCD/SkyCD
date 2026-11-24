using System;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

internal sealed class SplitLimitException()
    : ArgumentException("No split limit was set: provide a maximum entry count or a maximum part size.");
