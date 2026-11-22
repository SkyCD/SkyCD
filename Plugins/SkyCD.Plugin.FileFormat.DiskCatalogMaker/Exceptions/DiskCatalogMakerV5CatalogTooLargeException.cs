using System;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a catalog exceeds the maximum number of bytes the importer will materialize.
/// </summary>
public sealed class DiskCatalogMakerV5CatalogTooLargeException(long actualBytes, long maximumBytes)
    : InvalidOperationException(
        $"DiskCatalogMaker v5 catalog is {actualBytes} byte(s), which exceeds the supported maximum of {maximumBytes} byte(s).");
