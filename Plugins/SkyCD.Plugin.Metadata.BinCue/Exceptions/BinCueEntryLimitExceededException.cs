using System.IO;

namespace SkyCD.Plugin.Metadata.BinCue.Exceptions;

/// <summary>
/// Raised when a contained file grows past the configured extraction limit.
/// </summary>
public sealed class BinCueEntryLimitExceededException()
    : IOException("BIN/CUE image entry exceeds the configured size limit.");
