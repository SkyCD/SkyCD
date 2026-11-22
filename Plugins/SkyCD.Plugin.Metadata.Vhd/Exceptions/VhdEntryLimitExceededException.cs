using System.IO;

namespace SkyCD.Plugin.Metadata.Vhd.Exceptions;

/// <summary>
/// Raised when a contained file expands past the configured entry size limit while extracting.
/// </summary>
public sealed class VhdEntryLimitExceededException()
    : IOException("Disk image entry exceeds the configured size limit.");
