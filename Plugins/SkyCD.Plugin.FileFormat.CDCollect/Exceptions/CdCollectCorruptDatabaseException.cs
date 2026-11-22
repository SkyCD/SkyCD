using System;

namespace SkyCD.Plugin.FileFormat.CDCollect.Exceptions;

/// <summary>
/// Thrown when a CDcollect database file is corrupted, truncated, or unreadable.
/// </summary>
public sealed class CdCollectCorruptDatabaseException(string? reason = null)
    : InvalidOperationException(
        string.IsNullOrWhiteSpace(reason)
            ? "CDcollect database is corrupt or unreadable."
            : $"CDcollect database is corrupt or unreadable: {reason}");
