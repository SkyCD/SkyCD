using System;

namespace SkyCD.Plugin.FileFormat.Cathy.Exceptions;

/// <summary>
/// Thrown when a Cathy catalog contains no items or valid media entries.
/// </summary>
public sealed class CathyEmptyCatalogException(string message = "The Cathy catalog contains no folders or files.")
    : Exception(message);
