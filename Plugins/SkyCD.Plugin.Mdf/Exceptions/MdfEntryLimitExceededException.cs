using System.IO;

namespace SkyCD.Plugin.Mdf.Exceptions;

/// <summary>
/// Raised when a contained file grows past the configured extraction limit.
/// </summary>
public sealed class MdfEntryLimitExceededException()
    : IOException("MDF image entry exceeds the configured size limit.");
