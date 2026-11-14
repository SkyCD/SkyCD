using System;

namespace SkyCD.Plugin.Katalog.CsvIdx.Exceptions;

/// <summary>
/// Thrown when a specified Katalog collection folder or file contains no catalogs or records.
/// </summary>
public sealed class KatalogEmptyCollectionException(string message = "The Katalog collection folder contains no recognizable devices or catalog indexes.")
    : Exception(message);
