using System;

namespace SkyCD.Plugin.WinCatalog.Native.Exceptions;

/// <summary>
/// Thrown when a native WinCatalog database is truncated or not a valid SQLite file.
/// </summary>
public sealed class WinCatalogNativeCorruptException(string reason = "file is corrupt, truncated, or unreadable")
    : InvalidOperationException($"WinCatalog native catalog is corrupt, truncated, or unreadable: {reason}")
{
}
