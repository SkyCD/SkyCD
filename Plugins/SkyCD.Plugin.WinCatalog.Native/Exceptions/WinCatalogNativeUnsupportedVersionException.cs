using System;

namespace SkyCD.Plugin.WinCatalog.Native.Exceptions;

/// <summary>
/// Thrown when a native WinCatalog database reports an unsupported version.
/// </summary>
public sealed class WinCatalogNativeUnsupportedVersionException(string version)
    : InvalidOperationException($"Unsupported WinCatalog catalog version: {version}.")
{
}
