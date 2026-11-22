using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

public sealed class LegacyScdCatalog
{
    public List<LegacyScdEntry> Entries { get; } = [];
}