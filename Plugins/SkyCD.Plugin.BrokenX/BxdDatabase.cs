using System.Collections.Generic;

namespace SkyCD.Plugin.BrokenX;

/// <summary>
/// A Broken X database: root properties plus top-level item records.
/// </summary>
public sealed class BxdDatabase
{
    public List<BxdProperty> RootProperties { get; } = [];

    public List<BxdItem> Roots { get; } = [];
}
