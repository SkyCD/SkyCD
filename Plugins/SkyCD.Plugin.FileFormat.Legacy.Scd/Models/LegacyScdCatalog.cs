using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Legacy.Scd.Models;

public sealed class LegacyScdCatalog
{
    public List<LegacyScdEntry> Entries { get; } = [];
}