using System;

namespace SkyCD.Plugin.Cathy.Exceptions;

/// <summary>
/// Thrown when a Cathy catalog file header magic number is invalid or corrupted.
/// </summary>
public sealed class CathyCorruptHeaderException(string message = "The Cathy catalog file header is missing, corrupted, or has an invalid magic number.")
    : Exception(message);
