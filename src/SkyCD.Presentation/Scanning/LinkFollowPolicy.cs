namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Controls how directory symbolic links and reparse points are handled during catalog scans.
/// </summary>
/// <remarks>
/// Default is <see cref="FollowDirectoryLinksOnce"/>. Directory links are followed at most once
/// per stable visited identity (resolved final target path). Cycles are skipped, not followed again.
/// File links are cataloged as files and are not traversed as directories.
/// </remarks>
public enum LinkFollowPolicy
{
    /// <summary>
    /// Follow directory symbolic links / reparse points once, using stable visited identity for cycle detection.
    /// This is the default policy.
    /// </summary>
    FollowDirectoryLinksOnce = 0,

    /// <summary>
    /// Do not descend into directory symbolic links / reparse points. The link entry itself may still be listed.
    /// </summary>
    NeverFollowLinks = 1
}
