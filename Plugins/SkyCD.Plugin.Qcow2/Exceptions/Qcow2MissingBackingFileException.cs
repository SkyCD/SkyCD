using System.IO;

namespace SkyCD.Plugin.Qcow2.Exceptions;

/// <summary>
/// Raised when an overlay image needs a backing file that is missing or cannot be located next to it.
/// </summary>
public sealed class Qcow2MissingBackingFileException()
    : FileNotFoundException("QCOW2 image requires a backing file that is not available.");
