using System;

namespace SkyCD.Plugin.FileFormat.NeoFinder.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export contains no importable entries.
/// </summary>
public sealed class NeoFinderTextEmptyExportException()
    : InvalidOperationException("NeoFinder text export contains no importable entries.");
