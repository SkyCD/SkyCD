using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>What one scheduled extraction pass stored, reused or left untouched.</summary>
public sealed record MediaExtractionOutcome
{
    public required string EntryId { get; init; }

    public required string Fingerprint { get; init; }

    /// <summary>No provider ran because current captured records already existed.</summary>
    public bool Reused { get; init; }

    /// <summary>The source could not be read, so previously captured records were kept unchanged.</summary>
    public bool SourceUnavailable { get; init; }

    /// <summary>The scheduler owned the batch and published it; false when the caller publishes.</summary>
    public bool Published { get; init; }

    public string? BatchId { get; init; }

    /// <summary>Records written or reused for the current provider identities.</summary>
    public IReadOnlyList<MediaCacheRecord> Records { get; init; } = [];

    /// <summary>Previously captured records downgraded to stale because the source or provider changed.</summary>
    public IReadOnlyList<MediaCacheRecord> SupersededRecords { get; init; } = [];

    /// <summary>Captured records kept because the refresh attempt failed; failed states never replace data.</summary>
    public int PreservedRecordCount { get; init; }
}
