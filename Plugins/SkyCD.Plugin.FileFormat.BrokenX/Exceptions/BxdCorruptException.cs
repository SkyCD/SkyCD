using System;

namespace SkyCD.Plugin.FileFormat.BrokenX.Exceptions;

/// <summary>
/// Thrown when a Broken X database is structurally invalid, truncated or inconsistent.
/// </summary>
public sealed class BxdCorruptException(string detail)
    : InvalidOperationException($"Broken X database is corrupt: {detail}");
