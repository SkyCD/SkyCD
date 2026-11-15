using System.Collections.Generic;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Outcome of verifying stored artifact payloads against their portable references. Records whose
/// bytes are absent or do not match the recorded digest are reported and marked
/// <see cref="MediaCacheRecordState.Missing"/> so recovery can re-extract or restore them.
/// </summary>
public sealed record MediaCacheIntegrityResult(
    int VerifiedRecordCount,
    int MissingRecordCount,
    int CorruptedArtifactCount,
    IReadOnlyList<string> MissingRecordIds);
