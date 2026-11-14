using System;

namespace SkyCD.Plugin.CdCat.Hcf.Exceptions;

/// <summary>
/// Thrown when a CdCat catalog file contains no media entries.
/// </summary>
public sealed class CdCatEmptyCatalogException()
    : InvalidOperationException("CdCat catalog contains no media entries.");
