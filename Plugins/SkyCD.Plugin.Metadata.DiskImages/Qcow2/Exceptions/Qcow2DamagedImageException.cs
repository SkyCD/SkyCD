using System.IO;

namespace SkyCD.Plugin.Metadata.DiskImages.Qcow2.Exceptions;

/// <summary>
/// Raised when a QCOW2 image header, table, cluster, or backing reference is truncated or inconsistent.
/// </summary>
public sealed class Qcow2DamagedImageException()
    : IOException("QCOW2 image is damaged or truncated.");
