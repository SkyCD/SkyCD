using System.IO;

namespace SkyCD.Plugin.Metadata.XZ.Exceptions;

/// <summary>
/// Raised when the xz stream expands past the configured entry size limit while extracting.
/// </summary>
public sealed class XzEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
