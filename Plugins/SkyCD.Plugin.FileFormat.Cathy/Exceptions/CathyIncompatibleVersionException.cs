using System;

namespace SkyCD.Plugin.FileFormat.Cathy.Exceptions;

/// <summary>
/// Thrown when a Cathy catalog file format version is unsupported or incompatible.
/// </summary>
public sealed class CathyIncompatibleVersionException(short version)
    : Exception($"Incompatible Cathy catalog version: {version}.");
