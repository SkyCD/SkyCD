using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Models;

/// <summary>
/// Write payload that can export a whole database, selected volume subtrees, or
/// host catalog rows, optionally merging into an existing database.
/// </summary>
public sealed class DedExportRequest
{
    /// <summary>Gets the source database; required when <see cref="VolumeIds"/> is set.</summary>
    public DedDatabase? Database { get; init; }

    /// <summary>Gets host rows to convert when <see cref="Database"/> is absent.</summary>
    public IReadOnlyList<Dictionary<string, object?>>? Rows { get; init; }

    /// <summary>Gets the volume object ids to export; null exports the whole database.</summary>
    public IReadOnlyList<ulong>? VolumeIds { get; init; }

    /// <summary>Gets the omission options for volume export.</summary>
    public DedTransferOptions Options { get; init; } = DedTransferOptions.All;

    /// <summary>Gets an optional database that receives the exported volumes.</summary>
    public DedDatabase? ExistingDatabase { get; init; }

    /// <summary>Gets an optional title; defaults to the file name stem.</summary>
    public string? Title { get; init; }
}
