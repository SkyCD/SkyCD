using System.IO;

namespace SkyCD.Plugin.Metadata.Ace.Exceptions;

/// <summary>
/// Raised when a stored archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class AceEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
