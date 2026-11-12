using System;

namespace SkyCD.Plugin.WinCatalog.Csv.Exceptions;

/// <summary>
/// Thrown when a WinCatalog CSV report contains no data rows.
/// </summary>
public sealed class WinCatalogEmptyReportException()
    : InvalidOperationException("WinCatalog CSV report contains no data records to import.");
