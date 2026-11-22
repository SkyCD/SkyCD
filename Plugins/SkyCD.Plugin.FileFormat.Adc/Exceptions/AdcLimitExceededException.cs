using System;

namespace SkyCD.Plugin.FileFormat.Adc.Exceptions;

/// <summary>
/// Raised when a size, count or nesting value exceeds a documented safety limit.
/// </summary>
public sealed class AdcLimitExceededException(string what, long value, long limit)
    : InvalidOperationException($"Advanced Disk Catalog {what} {value} exceeds the limit {limit}.");
