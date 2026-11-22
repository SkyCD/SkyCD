using System.IO;

namespace SkyCD.Plugin.Metadata.Archives.Arc.Exceptions;

/// <summary>
/// Raised when an archive contains more entries than the configured limit.
/// </summary>
public sealed class ArcEntryCountExceededException()
    : IOException("Archive contains more entries than the configured limit.");
