using System.IO;

namespace SkyCD.Plugin.FileFormat.Lha.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class LhaEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
