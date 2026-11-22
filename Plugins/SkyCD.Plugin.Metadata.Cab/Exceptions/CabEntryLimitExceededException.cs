using System.IO;

namespace SkyCD.Plugin.Metadata.Cab.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class CabEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
