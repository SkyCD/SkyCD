using System.IO;

namespace SkyCD.Plugin.Metadata.Vdi.Exceptions;

/// <summary>
/// Raised when a snapshot or differencing VDI image needs a base image that is missing or cannot be
/// located next to it.
/// </summary>
public sealed class VdiMissingParentException()
    : FileNotFoundException("VDI image requires a parent image that is not available.");
