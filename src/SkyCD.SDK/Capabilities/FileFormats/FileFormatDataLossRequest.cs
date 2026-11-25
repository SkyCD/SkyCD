using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.FileFormats;

/// <summary>
/// Input for a preflight loss assessment. The same snapshot is used for the assessment and the write.
/// </summary>
/// <param name="FormatId">Format selected by host routing.</param>
/// <param name="FileName">Optional target file name.</param>
/// <param name="Entries">Entry snapshot included in the write.</param>
/// <param name="CatalogScopeItems">Catalog-wide data outside the entry tree that the host cannot route to a format.</param>
/// <param name="CultureName">Current UI culture name, for plugin-side formatting.</param>
public sealed record FileFormatDataLossRequest(
    string FormatId,
    string? FileName,
    IReadOnlyList<FileFormatDataLossEntry> Entries,
    IReadOnlyList<FileFormatDataLossItem> CatalogScopeItems,
    string CultureName);
