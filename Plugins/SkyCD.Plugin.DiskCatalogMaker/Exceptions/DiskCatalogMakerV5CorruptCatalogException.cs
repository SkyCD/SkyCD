using System;

namespace SkyCD.Plugin.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker v5 catalog violates the documented container or table layout.
/// </summary>
public sealed class DiskCatalogMakerV5CorruptCatalogException(string detail)
    : InvalidOperationException($"DiskCatalogMaker v5 catalog is corrupt: {detail}");
