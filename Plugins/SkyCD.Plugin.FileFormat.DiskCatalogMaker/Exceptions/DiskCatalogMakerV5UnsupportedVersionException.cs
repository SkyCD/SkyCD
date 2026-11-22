using System;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;

/// <summary>
/// Thrown when a catalog declares a DiskCatalogMaker format version other than v5.
/// </summary>
public sealed class DiskCatalogMakerV5UnsupportedVersionException(int major, int minor)
    : NotSupportedException(
        $"Unsupported DiskCatalogMaker catalog version {major}.{minor}; only format version 5 is supported.");
