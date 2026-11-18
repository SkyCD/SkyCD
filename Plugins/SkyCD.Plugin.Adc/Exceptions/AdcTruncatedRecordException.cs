using System;

namespace SkyCD.Plugin.Adc.Exceptions;

/// <summary>
/// Raised when a framed record or the file header ends before its declared payload does.
/// </summary>
public sealed class AdcTruncatedRecordException(int recordIndex, string what)
    : InvalidOperationException($"Advanced Disk Catalog is truncated in record {recordIndex}: {what}.");
