using System.IO;

namespace SkyCD.Plugin.Metadata.Nrg.Exceptions;

/// <summary>
/// Raised when an image entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class NrgEntryLimitExceededException()
    : IOException("NRG entry exceeds the configured size limit.");
