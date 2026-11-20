using System.IO;

namespace SkyCD.Plugin.Qcow2.Exceptions;

/// <summary>
/// Raised when a backing file reference resolves outside the directory of the image that names it.
/// </summary>
public sealed class Qcow2UnsafeCompanionPathException()
    : IOException("QCOW2 backing file references a path outside the image directory.");
