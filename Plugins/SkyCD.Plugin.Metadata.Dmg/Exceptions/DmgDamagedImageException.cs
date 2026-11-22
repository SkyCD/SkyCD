using System.IO;

namespace SkyCD.Plugin.Metadata.Dmg.Exceptions;

/// <summary>
/// Raised when a file does not contain a readable UDIF trailer or its partitions cannot be parsed.
/// </summary>
public sealed class DmgDamagedImageException()
    : IOException("The DMG image is damaged or is not a UDIF disk image.");
