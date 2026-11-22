using System.IO;

namespace SkyCD.Plugin.FileFormat.Vhd.Exceptions;

/// <summary>
/// Raised when a differencing image needs a parent file that is missing or cannot be located next to it.
/// </summary>
public sealed class VhdMissingParentException()
    : FileNotFoundException("Differencing disk image requires a parent file that is not available.");
