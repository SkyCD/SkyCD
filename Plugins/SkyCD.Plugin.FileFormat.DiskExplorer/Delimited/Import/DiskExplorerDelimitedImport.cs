using System.Collections.Generic;
using SkyCD.Plugin.FileFormat.DiskExplorer.Models;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Delimited.Import;

/// <summary>
/// Result of parsing a delimited-text report: the shared parse shape plus
/// whether the import is partial (entries were attached to a volume without
/// explicit path evidence, so no missing descendants are invented).
/// </summary>
public sealed record DiskExplorerDelimitedImport(
    DiskExplorerParseResult Result,
    bool IsPartial);
