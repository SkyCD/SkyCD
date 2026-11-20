using System.IO;

namespace SkyCD.Plugin.Qcow2.Exceptions;

/// <summary>
/// Raised when a contained file expands past the configured entry size limit while extracting.
/// </summary>
public sealed class Qcow2EntryLimitExceededException()
    : IOException("Disk image entry exceeds the configured size limit.");
