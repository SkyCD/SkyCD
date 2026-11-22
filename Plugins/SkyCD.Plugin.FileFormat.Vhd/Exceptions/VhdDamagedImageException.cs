using System.IO;

namespace SkyCD.Plugin.FileFormat.Vhd.Exceptions;

/// <summary>
/// Raised when a VHD or VHDX footer, header, block table, or metadata entry is truncated or inconsistent.
/// </summary>
public sealed class VhdDamagedImageException()
    : IOException("Disk image is damaged or truncated.");
