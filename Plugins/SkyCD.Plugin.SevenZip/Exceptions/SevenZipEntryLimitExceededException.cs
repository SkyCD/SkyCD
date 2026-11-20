using System.IO;

namespace SkyCD.Plugin.SevenZip.Exceptions;

/// <summary>
/// Raised when a 7z entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class SevenZipEntryLimitExceededException()
    : IOException("7z archive entry exceeds the configured size limit.");
