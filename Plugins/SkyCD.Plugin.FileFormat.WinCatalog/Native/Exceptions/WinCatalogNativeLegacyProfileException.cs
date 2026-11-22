using System;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Native.Exceptions;

/// <summary>
/// Thrown when a legacy WinCatalog collection profile is supplied instead of a native .w3cat.
/// </summary>
public sealed class WinCatalogNativeLegacyProfileException(string? fileName)
    : InvalidOperationException($"Legacy WinCatalog profile '{fileName ?? "unknown"}' requires conversion to .w3cat in WinCatalog or a CSV/XML report export; direct import is not supported.")
{
}
