using System.IO;

namespace SkyCD.Plugin.FileFormat.Gzip.Exceptions;

/// <summary>
/// Raised when the gzip member expands past the configured entry size limit while extracting.
/// </summary>
public sealed class GzipEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
