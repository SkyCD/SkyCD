using System;

namespace SkyCD.Plugin.Adc.Exceptions;

/// <summary>
/// Raised when the catalog format major version is not supported.
/// </summary>
public sealed class AdcUnsupportedVersionException(ushort major, ushort minor)
    : InvalidOperationException($"Advanced Disk Catalog version {major}.{minor} is unsupported; only version 1.x is supported.");
