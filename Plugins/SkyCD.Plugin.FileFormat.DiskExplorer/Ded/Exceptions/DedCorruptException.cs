using System;

namespace SkyCD.Plugin.FileFormat.DiskExplorer.Ded.Exceptions;

/// <summary>Raised when a DED container violates the documented layout.</summary>
public sealed class DedCorruptException(string detail)
    : InvalidOperationException($"Disk Explorer database is corrupt: {detail}");
