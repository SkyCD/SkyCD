using System;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker text export contains a header but no data rows.
/// </summary>
public sealed class DiskCatalogMakerEmptyReportException()
    : InvalidOperationException("DiskCatalogMaker text export contains no data rows. Export at least one catalog entry before importing.");
