using System.IO;

namespace SkyCD.Plugin.Zoo.Exceptions;

/// <summary>
/// Raised when a ZOO archive header, entry, or entry payload is malformed or fails its integrity check.
/// </summary>
public sealed class ZooDamagedArchiveException()
    : IOException("ZOO archive is damaged or has an unsupported layout.");
