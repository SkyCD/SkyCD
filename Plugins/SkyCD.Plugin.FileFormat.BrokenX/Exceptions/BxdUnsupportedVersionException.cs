using System;

namespace SkyCD.Plugin.FileFormat.BrokenX.Exceptions;

/// <summary>
/// Thrown when a Broken X database uses an archive version other than <c>0x40</c>.
/// </summary>
public sealed class BxdUnsupportedVersionException(byte version)
    : InvalidOperationException($"Broken X database uses unsupported archive version 0x{version:X2}; only version 0x40 is supported.");
