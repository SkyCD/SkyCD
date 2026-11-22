using System;
using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Vmdk.Exceptions;

/// <summary>
/// Raised when a VMDK descriptor references an extent or parent disk outside the image directory.
/// </summary>
public sealed class VmdkUnsafeExtentPathException()
    : IOException("VMDK descriptor references a file outside the image directory.");
