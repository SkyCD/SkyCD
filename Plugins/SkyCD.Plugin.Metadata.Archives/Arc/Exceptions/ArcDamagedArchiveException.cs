using System.IO;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Exceptions;

/// <summary>
/// Raised when an ARC archive is damaged, truncated, or missing its end-of-archive marker.
/// </summary>
public sealed class ArcDamagedArchiveException()
    : IOException("ARC archive is damaged or truncated.");
