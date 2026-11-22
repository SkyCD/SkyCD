using System.IO;

namespace SkyCD.Plugin.Metadata.Bzip2.Exceptions;

/// <summary>
/// Raised when the bzip2 stream expands past the configured entry size limit while extracting.
/// </summary>
public sealed class Bzip2EntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
