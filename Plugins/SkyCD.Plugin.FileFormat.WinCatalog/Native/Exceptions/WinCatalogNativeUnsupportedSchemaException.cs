using System;

namespace SkyCD.Plugin.FileFormat.WinCatalog.Native.Exceptions;

/// <summary>
/// Thrown when a SQLite database lacks WinCatalog catalog markers.
/// </summary>
public sealed class WinCatalogNativeUnsupportedSchemaException(string reason)
    : InvalidOperationException($"Unsupported WinCatalog database schema: {reason}")
{
}
