using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when an in-memory DED model cannot be serialized safely.</summary>
public sealed class DedModelException(string detail)
    : InvalidOperationException($"Disk Explorer database model is invalid: {detail}");
