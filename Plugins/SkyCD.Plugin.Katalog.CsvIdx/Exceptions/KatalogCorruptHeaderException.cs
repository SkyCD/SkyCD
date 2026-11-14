using System;

namespace SkyCD.Plugin.Katalog.CsvIdx.Exceptions;

/// <summary>
/// Thrown when a Katalog device or catalog index file header is missing or unparseable.
/// </summary>
public sealed class KatalogCorruptHeaderException(string message = "The Katalog file header is missing, corrupted, or invalid.")
    : Exception(message);
