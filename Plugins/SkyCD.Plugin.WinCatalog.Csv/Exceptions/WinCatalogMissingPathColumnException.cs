using System;

namespace SkyCD.Plugin.WinCatalog.Csv.Exceptions;

/// <summary>
/// Thrown when a WinCatalog CSV report is missing essential path columns needed to reconstruct hierarchy.
/// </summary>
public sealed class WinCatalogMissingPathColumnException()
    : InvalidOperationException("WinCatalog CSV report is missing essential path columns ('Catalog Path' or 'File Path'). Reconstructing hierarchy is not possible without path information.");
