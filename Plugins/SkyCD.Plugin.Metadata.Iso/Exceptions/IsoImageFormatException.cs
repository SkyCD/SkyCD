using System.IO;

namespace SkyCD.Plugin.Metadata.Iso.Exceptions;

/// <summary>
/// Raised when an image is damaged or its metadata cannot be read as an ISO 9660 or UDF filesystem.
/// </summary>
public sealed class IsoImageFormatException(string detail)
    : IOException($"The disk image could not be read: {detail}.");
