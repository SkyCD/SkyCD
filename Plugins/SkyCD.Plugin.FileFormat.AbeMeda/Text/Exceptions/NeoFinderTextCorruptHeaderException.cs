using System;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Text.Exceptions;

/// <summary>
/// Thrown when a NeoFinder text export has no readable header row.
/// </summary>
public sealed class NeoFinderTextCorruptHeaderException()
    : InvalidOperationException("NeoFinder text export has no readable header row.");
