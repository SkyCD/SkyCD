using System.Collections.Generic;

namespace SkyCD.Presentation.Catalog.Export;

/// <summary>An export format the export dialog offers. Only write-only formats are offered: readable
/// catalog formats stay in Save As, and nothing here can be opened back as a catalog.</summary>
/// <param name="FormatId">The format the catalog is exported in.</param>
/// <param name="DisplayName">The name shown in the format picker.</param>
/// <param name="DefaultExtension">The extension the suggested file name defaults to, without a leading dot.</param>
/// <param name="Extensions">Every extension the format writes, with or without a leading dot.</param>
public sealed record ExportFormatOption(
    string FormatId,
    string DisplayName,
    string DefaultExtension,
    IReadOnlyList<string> Extensions);
