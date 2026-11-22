using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export layout is ambiguous and cannot be mapped reliably.
/// </summary>
public sealed class NeoFinderTextAmbiguousLayoutException(string detail)
    : InvalidOperationException($"NeoFinder text export layout is ambiguous and was not imported. {detail} Export with \"Text with Tab\", headers included, and check the Include Fields selection.");
