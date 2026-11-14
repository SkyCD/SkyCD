using System;

namespace SkyCD.Plugin.CdCat.Hcf.Exceptions;

/// <summary>
/// Thrown when a CdCat catalog specifies a version greater than the maximum supported version (2.1).
/// </summary>
public sealed class CdCatUnsupportedVersionException(string? version = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(version)
            ? "Unsupported CdCat catalog version. Supported versions are up to 2.1."
            : $"Unsupported CdCat catalog version '{version}'. Supported versions are up to 2.1.");
