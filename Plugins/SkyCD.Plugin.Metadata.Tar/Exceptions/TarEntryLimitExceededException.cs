using System.IO;

namespace SkyCD.Plugin.Metadata.Tar.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class TarEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
