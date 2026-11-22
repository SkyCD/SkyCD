using System.IO;

namespace SkyCD.Plugin.FileFormat.Ccd.Exceptions;

/// <summary>
/// Raised when a contained file grows past the configured extraction limit.
/// </summary>
public sealed class CcdEntryLimitExceededException()
    : IOException("CCD image entry exceeds the configured size limit.");
