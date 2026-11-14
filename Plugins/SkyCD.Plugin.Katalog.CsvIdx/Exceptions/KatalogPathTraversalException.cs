using System;

namespace SkyCD.Plugin.Katalog.CsvIdx.Exceptions;

/// <summary>
/// Thrown when an invalid or insecure relative path traversing outside the collection is encountered.
/// </summary>
public sealed class KatalogPathTraversalException(string path)
    : Exception($"Relative path '{path}' attempts to traverse outside the collection root.");
