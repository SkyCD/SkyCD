namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Outcome of a host-driven plugin configuration flow.
/// </summary>
public enum PluginConfigurationStatus
{
    /// <summary>The user accepted the configuration and the settings were persisted.</summary>
    Succeeded,

    /// <summary>The user dismissed the configuration surface without accepting changes.</summary>
    Canceled,

    /// <summary>The selected plugin does not implement the configuration capability.</summary>
    NotSupported,

    /// <summary>The plugin reported a failure or the host caught a plugin exception.</summary>
    Failed,

    /// <summary>The configuration surface did not complete within the host timeout.</summary>
    TimedOut
}
