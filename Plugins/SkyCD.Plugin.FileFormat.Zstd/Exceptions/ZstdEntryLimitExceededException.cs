using System.IO;

namespace SkyCD.Plugin.FileFormat.Zstd.Exceptions;

/// <summary>
/// Raised when the Zstandard stream expands past the configured entry size limit while extracting.
/// </summary>
public sealed class ZstdEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
