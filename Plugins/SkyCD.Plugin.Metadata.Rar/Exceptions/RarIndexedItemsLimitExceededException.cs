using System;

namespace SkyCD.Plugin.Metadata.Rar.Exceptions;

/// <summary>
/// Raised when the number of entries kept for indexing grows past the configured entry count limit.
/// </summary>
public sealed class RarIndexedItemsLimitExceededException()
    : InvalidOperationException("Archive contains more indexable entries than the configured limit.");
