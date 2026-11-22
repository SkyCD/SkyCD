using System;

namespace SkyCD.Plugin.FileFormat.CDCollect.Exceptions;

/// <summary>
/// Thrown when the file format or schema is invalid for a CDcollect database.
/// </summary>
public sealed class CdCollectInvalidFormatException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "Invalid CDcollect database format."
            : $"Invalid CDcollect database format: {reason}");
