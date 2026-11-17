using System;

namespace SkyCD.Plugin.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker text export is missing the Path column needed to reconstruct hierarchy.
/// </summary>
public sealed class DiskCatalogMakerMissingPathColumnException()
    : InvalidOperationException("DiskCatalogMaker text export is missing the 'Path' column. Enable the Path attribute in DiskCatalogMaker's list view and export again; reconstructing hierarchy is not possible without path information.");
