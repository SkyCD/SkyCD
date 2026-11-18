using System;

namespace SkyCD.Plugin.Adc.Exceptions;

/// <summary>
/// Raised when a header, comment-table or string offset points outside the file.
/// </summary>
public sealed class AdcMalformedOffsetException(string field, long value)
    : InvalidOperationException($"Advanced Disk Catalog has a malformed {field} offset: {value}.");
