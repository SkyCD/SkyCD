using System;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;

/// <summary>
/// Raised when a CD Catalog Expert catalog XML member violates the documented
/// index or disc-tree grammar.
/// </summary>
public sealed class ZdcFormatException(string detail)
    : InvalidOperationException($"CD Catalog Expert catalog is malformed: {detail}");
