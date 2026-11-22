using System;

namespace SkyCD.Plugin.FileFormat.Cathy.Exceptions;

/// <summary>
/// Thrown when a Cathy catalog contains corrupted data structures or premature stream termination.
/// </summary>
public sealed class CathyInvalidCatalogDataException(string message = "The Cathy catalog contains corrupted records or invalid data structures.")
    : Exception(message);
