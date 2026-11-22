using System;

namespace SkyCD.Plugin.FileFormat.Adc.Exceptions;

/// <summary>
/// Raised when two records declare the same volume, item or category identifier.
/// </summary>
public sealed class AdcDuplicateIdException(string kind, uint id)
    : InvalidOperationException($"Advanced Disk Catalog contains duplicate {kind} id {id}.");
