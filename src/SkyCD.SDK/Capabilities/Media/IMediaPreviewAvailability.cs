namespace SkyCD.SDK.Capabilities.Media;

/// <summary>
/// Optional preview-availability report for preview capabilities (issue #1202). A capability
/// whose decoder may be absent (an undeployed native binding, a platform without the runtime)
/// implements this so the host can log one clearly named warning per plugin and skip the
/// provider, instead of failing every item it would have rendered.
/// </summary>
public interface IMediaPreviewAvailability
{
    /// <summary>
    /// Short, user-safe reason why no preview can be produced (no paths or user data), or null
    /// when previews are available.
    /// </summary>
    string? PreviewUnavailableReason { get; }
}
