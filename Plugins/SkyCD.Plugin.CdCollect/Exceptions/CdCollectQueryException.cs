using System;

namespace SkyCD.Plugin.CdCollect.Exceptions;

/// <summary>
/// Thrown when an error occurs while querying the CDcollect database.
/// </summary>
public sealed class CdCollectQueryException(string message, Exception? inner = null)
    : InvalidOperationException($"Error querying CDcollect database: {message}", inner);
