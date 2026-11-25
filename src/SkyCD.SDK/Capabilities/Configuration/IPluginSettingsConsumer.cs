using System.Collections.Generic;

namespace SkyCD.SDK.Capabilities.Configuration;

/// <summary>
/// Capability that runs from host-persisted settings rather than only when its configuration surface is
/// opened. The host applies the values saved for the plugin before invoking the capability, so an option
/// changed in an Options plugin page affects later extraction without a restart.
/// </summary>
/// <remarks>
/// The dictionary contains string values keyed exactly as they were persisted by
/// <see cref="IPluginConfigurationCapability"/>; missing keys mean defaults. Implementations must tolerate
/// unknown keys, null values and repeated calls, and must publish nothing when collection is disabled.
/// </remarks>
public interface IPluginSettingsConsumer : IPluginCapability
{
    /// <summary>Applies the persisted settings of the owning plugin.</summary>
    void ApplySettings(IReadOnlyDictionary<string, string?> settings);
}
