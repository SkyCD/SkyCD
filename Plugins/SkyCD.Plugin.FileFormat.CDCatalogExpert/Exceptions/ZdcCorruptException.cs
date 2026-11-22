using System;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;

/// <summary>
/// Raised when a CD Catalog Expert container violates the ZDC profile.
/// </summary>
public sealed class ZdcCorruptException(string detail)
    : InvalidOperationException($"CD Catalog Expert catalog is corrupt: {detail}");
