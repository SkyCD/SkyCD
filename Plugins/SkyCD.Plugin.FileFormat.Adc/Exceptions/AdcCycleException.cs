using System;

namespace SkyCD.Plugin.FileFormat.Adc.Exceptions;

/// <summary>
/// Raised when item parent references form a cycle instead of a tree.
/// </summary>
public sealed class AdcCycleException(uint itemId)
    : InvalidOperationException($"Advanced Disk Catalog hierarchy contains a parent cycle at item {itemId}.");
