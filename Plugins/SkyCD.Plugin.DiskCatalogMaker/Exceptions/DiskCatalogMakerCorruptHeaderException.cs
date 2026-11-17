using System;

namespace SkyCD.Plugin.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a DiskCatalogMaker text-export header row is invalid or empty.
/// </summary>
public sealed class DiskCatalogMakerCorruptHeaderException()
    : InvalidOperationException("DiskCatalogMaker text-export header row is invalid, empty, or unreadable.");
