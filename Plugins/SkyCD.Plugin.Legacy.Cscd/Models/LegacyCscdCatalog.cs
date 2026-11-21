using System.Collections.Generic;

namespace SkyCD.Plugin.Legacy.Cscd.Models;

public sealed class LegacyCscdCatalog
{
    public List<LegacyCscdEntry> Entries { get; } = [];
}