using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export contains no importable entries.
/// </summary>
public sealed class NeoFinderTextEmptyExportException()
    : InvalidOperationException("NeoFinder text export contains no importable entries.");
