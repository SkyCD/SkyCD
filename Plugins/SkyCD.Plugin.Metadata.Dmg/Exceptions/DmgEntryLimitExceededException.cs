using System.IO;

namespace SkyCD.Plugin.Metadata.Dmg.Exceptions;

/// <summary>
/// Raised when a contained file grows past the configured extraction limit.
/// </summary>
public sealed class DmgEntryLimitExceededException()
    : IOException("DMG image entry exceeds the configured size limit.");
