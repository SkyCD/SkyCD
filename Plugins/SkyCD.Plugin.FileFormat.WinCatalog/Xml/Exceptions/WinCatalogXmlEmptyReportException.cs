using System;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Xml.Exceptions;

/// <summary>
/// Thrown when a WinCatalog XML report parses successfully but contains no importable items.
/// </summary>
public sealed class WinCatalogXmlEmptyReportException()
    : InvalidOperationException("WinCatalog XML report contains no importable disk, folder, or file items.");
