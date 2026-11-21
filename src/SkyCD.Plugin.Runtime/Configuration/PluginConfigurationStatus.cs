namespace SkyCD.Plugin.Runtime.Configuration;

/// <summary>
/// Outcome of a host-driven plugin configuration flow.
/// </summary>
public enum PluginConfigurationStatus
{
    /// <summary>The requested creation or validation operation succeeded. No implicit persistence.</summary>
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
