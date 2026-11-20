using System.IO;

namespace SkyCD.Plugin.Iso.Exceptions;

/// <summary>
/// Raised when an image entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class IsoEntryLimitExceededException()
    : IOException("ISO image entry exceeds the configured size limit.");
