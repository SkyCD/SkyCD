using System.IO;

namespace SkyCD.Plugin.FileFormat.Vdi.Exceptions;

/// <summary>
/// Raised when a VirtualBox VDI header, block map, or block entry is truncated or inconsistent.
/// </summary>
public sealed class VdiDamagedImageException()
    : IOException("VDI image is damaged or truncated.");
