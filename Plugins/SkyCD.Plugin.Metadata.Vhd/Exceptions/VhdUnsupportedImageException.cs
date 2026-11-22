using System;

namespace SkyCD.Plugin.Metadata.Vhd.Exceptions;

/// <summary>
/// Raised when an image uses a VHD or VHDX feature this reader does not support, such as an unknown
/// signature, an unusual parent locator, or a chain that is too deep to resolve safely.
/// </summary>
public sealed class VhdUnsupportedImageException()
    : NotSupportedException("Disk image uses a feature that is not supported.");
