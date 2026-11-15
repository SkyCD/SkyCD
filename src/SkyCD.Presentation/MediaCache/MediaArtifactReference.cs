namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Portable, content-addressed reference to a cached artifact payload. References never contain
/// filesystem paths or machine-specific locations, so they stay valid across export, backup and
/// restore; the bytes are resolved by <see cref="IMediaCacheStore"/>.
/// </summary>
public sealed class MediaArtifactReference
{
    /// <summary>Record-local key, for example <c>preview:0</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long Length { get; set; }

    /// <summary>Lowercase hex SHA-256 of the payload, used to detect missing or corrupted artifacts.</summary>
    public string Sha256 { get; set; } = string.Empty;
}
