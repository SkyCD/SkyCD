using System.Collections.Generic;

namespace SkyCD.Plugin.FileFormat.Cathy.Models;

/// <summary>
/// A normalized node representation of the Cathy catalog hierarchy.
/// </summary>
public sealed record CathyNode(
    string Name,
    string FullPath,
    long Size,
    bool IsDirectory,
    string? ModifiedDate,
    IReadOnlyList<CathyNode> Children);
