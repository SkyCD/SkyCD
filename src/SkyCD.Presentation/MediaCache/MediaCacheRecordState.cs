namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Lifecycle state of one cached extraction result. Every state except <see cref="Unsupported"/>
/// keeps its payload readable while the original media is disconnected or the plugin is gone.
/// </summary>
public enum MediaCacheRecordState
{
    /// <summary>Captured from the current source fingerprint by a currently registered provider.</summary>
    Captured = 0,

    /// <summary>Still readable offline, but the source fingerprint or the provider identity changed.</summary>
    Stale = 1,

    /// <summary>The provider ran and failed or timed out; no payload exists for this identity.</summary>
    Failed = 2,

    /// <summary>No enabled provider supports the source, recorded so passes do not retry forever.</summary>
    Unsupported = 3,

    /// <summary>The record exists but its artifact bytes are unavailable and must be recovered.</summary>
    Missing = 4
}
