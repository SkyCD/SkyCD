namespace SkyCD.UI.Controls.Preview;

/// <summary>
/// What the preview surface can currently show for the selected catalog entry. Every state is derived
/// from stored results only, so the surface stays meaningful while the media is disconnected or the
/// producing plugin is disabled.
/// </summary>
public enum MediaPreviewState
{
    /// <summary>No entry is selected.</summary>
    NoSelection = 0,

    /// <summary>Stored results are being read and decoded.</summary>
    Loading,

    /// <summary>A current preview, typed fields, or both are shown.</summary>
    Ready,

    /// <summary>Only superseded results are available; they stay readable offline.</summary>
    Stale,

    /// <summary>Nothing was ever captured for the entry.</summary>
    Unavailable,

    /// <summary>No enabled plugin supports the entry.</summary>
    Unsupported,

    /// <summary>Every plugin that ran failed or timed out.</summary>
    Failed,

    /// <summary>A record exists but its artifact bytes are unavailable.</summary>
    MissingArtifact,

    /// <summary>The stored artifact exceeds the display bound and was not decoded.</summary>
    TooLarge
}
