using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Archives;
using SkyCD.Plugin.Host.Media;

namespace SkyCD.Plugin.Host.Archives;

/// <summary>
/// Metadata collected for one extracted archive entry, keyed by the entry path inside the archive.
/// </summary>
public sealed record ArchiveEntryMetadata(string Path, IReadOnlyList<MediaProviderResult> Results);

/// <summary>
/// Result of extracting an archive and running enabled metadata plugins over its entries.
/// </summary>
public sealed record ArchiveIndexResult(
    ArchiveInspectionResult Inspection,
    IReadOnlyList<ArchiveEntryMetadata> EntryMetadata);
