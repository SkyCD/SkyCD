namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Controls whether hidden and/or system files and directories are included in catalog scans.
/// </summary>
public enum HiddenSystemFilePolicy
{
    /// <summary>
    /// Include hidden and system entries. This is the default for compatibility with existing scans.
    /// </summary>
    Include = 0,

    /// <summary>
    /// Skip entries with the Hidden attribute.
    /// </summary>
    SkipHidden = 1,

    /// <summary>
    /// Skip entries with the System attribute.
    /// </summary>
    SkipSystem = 2,

    /// <summary>
    /// Skip entries with Hidden or System attributes.
    /// </summary>
    SkipHiddenAndSystem = 3
}
