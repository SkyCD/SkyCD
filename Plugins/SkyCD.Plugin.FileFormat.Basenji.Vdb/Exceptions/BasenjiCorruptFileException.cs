using System;

namespace SkyCD.Plugin.FileFormat.Basenji.Vdb.Exceptions;

/// <summary>
/// Thrown when a Basenji catalog file is corrupted, unreadable, or not a valid SQLite database structure.
/// </summary>
public sealed class BasenjiCorruptFileException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "Basenji catalog file is corrupt or unreadable."
            : $"Basenji catalog file is corrupt or unreadable: {reason}");
