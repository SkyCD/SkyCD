using System.IO;

namespace SkyCD.Plugin.Metadata.Arj.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class ArjEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
