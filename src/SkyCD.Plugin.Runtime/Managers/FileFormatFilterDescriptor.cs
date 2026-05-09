using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Managers;

public sealed record FileFormatFilterDescriptor(
    string DisplayName,
    IReadOnlyList<string> Patterns);
