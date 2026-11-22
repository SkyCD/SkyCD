using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

public sealed class LegacyAscdCatalog
{
    public string HeaderVersion { get; init; } = "1.0";

    public List<LegacyAscdEntry> Entries { get; } = [];
}