using System.IO;

namespace SkyCD.Plugin.Iso.Exceptions;

/// <summary>
/// Raised when an image contains neither a supported ISO 9660 nor a supported UDF volume.
/// </summary>
public sealed class IsoUnsupportedImageException(string detail)
    : IOException($"The disk image is not supported: {detail}.");
