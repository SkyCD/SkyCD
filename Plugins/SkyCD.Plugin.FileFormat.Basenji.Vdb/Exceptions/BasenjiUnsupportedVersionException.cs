using System;

namespace SkyCD.Plugin.FileFormat.Basenji.Vdb.Exceptions;

/// <summary>
/// Thrown when a Basenji catalog specifies a version greater than the maximum supported version (4).
/// </summary>
public sealed class BasenjiUnsupportedVersionException(string? version = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(version)
            ? "Unsupported Basenji catalog version. Supported versions are up to 4."
            : $"Unsupported Basenji catalog version '{version}'. Supported versions are up to 4.");
