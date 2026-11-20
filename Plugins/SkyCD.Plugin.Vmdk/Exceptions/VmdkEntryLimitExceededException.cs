using System;
using System.IO;

namespace SkyCD.Plugin.Vmdk.Exceptions;

/// <summary>
/// Raised when a VMDK entry expands past the configured entry size limit while extracting.
/// </summary>
public sealed class VmdkEntryLimitExceededException()
    : IOException("Disk image entry exceeds the configured size limit.");
