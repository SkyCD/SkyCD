using System;

namespace SkyCD.Plugin.FileFormat.Vdi.Exceptions;

/// <summary>
/// Raised when an image uses a VirtualBox VDI feature this reader does not support, such as an unknown
/// signature, an encrypted image, a future header version, or a chain that is too deep to resolve.
/// </summary>
public sealed class VdiUnsupportedImageException()
    : NotSupportedException("VDI image uses a feature that is not supported.");
