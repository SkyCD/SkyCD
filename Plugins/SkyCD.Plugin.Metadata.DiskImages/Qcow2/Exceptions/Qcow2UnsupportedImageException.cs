using System;

namespace SkyCD.Plugin.Metadata.DiskImages.Qcow2.Exceptions;

/// <summary>
/// Raised when an image uses a QCOW2 feature this reader does not support, such as zstd compression,
/// extended L2 entries, or an external data file.
/// </summary>
public sealed class Qcow2UnsupportedImageException()
    : NotSupportedException("QCOW2 image uses a feature that is not supported.");
