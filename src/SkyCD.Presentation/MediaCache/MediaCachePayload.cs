using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// A cache record together with the artifact bytes it references. Artifact keys that are omitted
/// keep the bytes already stored for that record, so state-only updates never drop payloads.
/// </summary>
public sealed class MediaCachePayload
{
    public MediaCachePayload(MediaCacheRecord record, IReadOnlyDictionary<string, byte[]>? artifacts = null)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Artifacts = artifacts ?? new Dictionary<string, byte[]>();
    }

    public MediaCacheRecord Record { get; }

    public IReadOnlyDictionary<string, byte[]> Artifacts { get; }
}
