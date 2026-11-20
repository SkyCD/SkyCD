using System.IO;

namespace SkyCD.Plugin.Vhd.Exceptions;

/// <summary>
/// Raised when a parent file reference resolves outside the directory of the image that names it.
/// </summary>
public sealed class VhdUnsafeCompanionPathException()
    : IOException("Differencing disk image references a parent file outside the image directory.");
