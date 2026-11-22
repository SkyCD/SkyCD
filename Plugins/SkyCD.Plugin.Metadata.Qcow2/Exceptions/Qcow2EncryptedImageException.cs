using System;

namespace SkyCD.Plugin.Metadata.Qcow2.Exceptions;

/// <summary>
/// Raised when an image is encrypted and its clusters therefore cannot be read.
/// </summary>
public sealed class Qcow2EncryptedImageException()
    : NotSupportedException("QCOW2 image is encrypted.");
