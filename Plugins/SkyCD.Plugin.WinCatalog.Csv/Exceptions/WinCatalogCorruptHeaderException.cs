using System;

namespace SkyCD.Plugin.WinCatalog.Csv.Exceptions;

/// <summary>
/// Thrown when a WinCatalog CSV report header row is invalid or empty.
/// </summary>
public sealed class WinCatalogCorruptHeaderException()
    : InvalidOperationException("WinCatalog CSV report header row is invalid, empty, or unreadable.");
