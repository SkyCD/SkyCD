using System.IO;

namespace SkyCD.Plugin.Metadata.Vdi.Exceptions;

/// <summary>
/// Raised when a contained file expands past the configured entry size limit while extracting.
/// </summary>
public sealed class VdiEntryLimitExceededException()
    : IOException("VDI image entry exceeds the configured size limit.");
