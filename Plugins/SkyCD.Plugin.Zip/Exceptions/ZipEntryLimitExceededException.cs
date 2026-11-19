using System;
using System.IO;

namespace SkyCD.Plugin.Zip.Exceptions;

/// <summary>
/// Raised when an archive entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class ZipEntryLimitExceededException()
    : IOException("Archive entry exceeds the configured size limit.");
