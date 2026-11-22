using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

public sealed class LegacyCscdCatalog
{
    public List<LegacyCscdEntry> Entries { get; } = [];
}