namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Lifecycle state of a catalog plugin install or update operation.
/// </summary>
public enum PluginInstallOperationStatus
{
    /// <summary>No catalog operation is known for the plugin.</summary>
    NotInstalled = 0,

    /// <summary>The package is downloaded and validated and activates on the next host restart.</summary>
    PendingRestart,

    /// <summary>The catalog plugin is installed, active and has no pending change.</summary>
    Installed,

    /// <summary>The last operation failed; the previously active version, if any, is preserved.</summary>
    Failed,

    /// <summary>A replacement failed and the previously active version was restored.</summary>
    RolledBack,

    /// <summary>A staged operation was discarded without changing the active version.</summary>
    Cancelled
}
