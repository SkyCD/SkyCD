using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Indexing;

namespace SkyCD.Plugin.Metadata.Archives.Models;

/// <summary>The work items one archive pass contributes plus the entries it skipped.</summary>
internal sealed record ArchiveIndexResult(IndexWorkItemSet WorkItems, IReadOnlyList<ArchiveSkipEntry> Skipped)
{
    /// <summary>Gets a result that contributes nothing and owns no resources.</summary>
    public static ArchiveIndexResult Empty { get; } = new(IndexWorkItemSet.Empty, []);

    /// <summary>Gets a set that contributes nothing and reports one typed archive-level failure code.</summary>
    public static ArchiveIndexResult Failure(string code, string reason) =>
        new(IndexWorkItemSet.Empty, [new ArchiveSkipEntry(string.Empty, code, reason)]);
}
