using System.IO;

namespace SkyCD.Plugin.FileFormat.Cab.Exceptions;

/// <summary>
/// Raised when a cabinet header, file record, or data block is malformed or truncated.
/// </summary>
public sealed class CabDamagedException()
    : IOException("The cabinet archive is damaged or truncated.");
