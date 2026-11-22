using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.BrokenX.Models;

namespace SkyCD.Plugin.FileFormat.BrokenX.Parsing;

/// <summary>
/// A Broken X database: root properties plus top-level item records.
/// </summary>
public sealed class BxdDatabase
{
    public List<BxdProperty> RootProperties { get; } = [];

    public List<BxdItem> Roots { get; } = [];
}
