using System.IO;

namespace SkyCD.Plugin.FileFormat.Rar.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class RarEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
