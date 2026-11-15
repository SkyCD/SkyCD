namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// What a portable media cache bundle import restored. Records whose artifacts are absent or fail digest
/// verification are imported as <see cref="MediaCacheRecordState.Missing"/> so recovery can re-extract them.
/// </summary>
public sealed record MediaCacheBundleImportResult(
    int ImportedRecordCount,
    int SkippedRecordCount,
    int RestoredArtifactCount,
    int MissingArtifactCount,
    int IncompleteRecordCount);
