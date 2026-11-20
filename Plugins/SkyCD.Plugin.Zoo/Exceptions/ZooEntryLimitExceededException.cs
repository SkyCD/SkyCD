using System.IO;

namespace SkyCD.Plugin.Zoo.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class ZooEntryLimitExceededException()
    : IOException("ZOO archive entry exceeds the configured size limit.");
