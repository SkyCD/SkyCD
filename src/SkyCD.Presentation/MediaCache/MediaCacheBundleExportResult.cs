namespace SkyCD.Presentation.MediaCache;

/// <summary>What a portable media cache bundle export wrote.</summary>
public sealed record MediaCacheBundleExportResult(
    string Directory,
    int RecordCount,
    int ArtifactCount,
    int SkippedArtifactCount,
    long ArtifactBytes);
