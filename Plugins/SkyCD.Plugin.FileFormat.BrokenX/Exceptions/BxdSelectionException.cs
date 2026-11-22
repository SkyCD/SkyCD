using System;

namespace SkyCD.Plugin.FileFormat.BrokenX.Exceptions;

/// <summary>
/// Thrown when a Broken X export selection is empty or names unknown records.
/// </summary>
public sealed class BxdSelectionException(string detail)
    : InvalidOperationException($"Cannot export the Broken X selection: {detail}");
