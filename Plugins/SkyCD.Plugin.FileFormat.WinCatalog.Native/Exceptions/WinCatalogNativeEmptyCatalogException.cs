using System;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Native.Exceptions;

/// <summary>
/// Thrown when a native WinCatalog database contains no importable catalog rows.
/// </summary>
public sealed class WinCatalogNativeEmptyCatalogException()
    : InvalidOperationException("WinCatalog native catalog contains no importable entries.");
