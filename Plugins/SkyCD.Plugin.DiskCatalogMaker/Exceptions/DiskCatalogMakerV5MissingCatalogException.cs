using System;
using System.IO;

namespace SkyCD.Plugin.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker package does not contain the required Catalog.dcm payload.
/// </summary>
public sealed class DiskCatalogMakerV5MissingCatalogException(string packagePath)
    : FileNotFoundException(
        $"DiskCatalogMaker package '{packagePath}' does not contain the required Catalog.dcm catalog payload.",
        packagePath);
