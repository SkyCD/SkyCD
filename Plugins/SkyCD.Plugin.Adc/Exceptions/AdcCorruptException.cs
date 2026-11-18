using System;

namespace SkyCD.Plugin.Adc.Exceptions;

/// <summary>
/// Raised when an Advanced Disk Catalog violates the documented container structure.
/// </summary>
public sealed class AdcCorruptException(string detail)
    : InvalidOperationException($"Advanced Disk Catalog is corrupt: {detail}");
