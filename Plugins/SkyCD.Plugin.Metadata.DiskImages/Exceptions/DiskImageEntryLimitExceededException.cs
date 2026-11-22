using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Exceptions;

/// <summary>
/// Raised when a disk-image entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class DiskImageEntryLimitExceededException()
    : IOException("Disk image entry exceeds the configured size limit.");
