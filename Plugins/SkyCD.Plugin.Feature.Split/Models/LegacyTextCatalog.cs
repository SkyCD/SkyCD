using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;

namespace SkyCD.Plugin.Feature.Split.Models;

/// <summary>
/// A legacy text catalog read from disk together with the serialized byte length of every entry line, which
/// is what the part size limit is measured against.
/// </summary>
public sealed record LegacyTextCatalog(
    IReadOnlyList<LegacyScdEntry> Entries,
    IReadOnlyList<int> LineBytes,
    long TotalBytes)
{
    /// <summary>Gets the number of catalog entries.</summary>
    public int EntryCount => Entries.Count;
}
