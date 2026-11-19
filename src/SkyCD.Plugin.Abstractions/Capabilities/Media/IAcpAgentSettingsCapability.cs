using System.Collections.Generic;

namespace SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>Exposes local ACP provider selection to the host's Options dialog.</summary>
public interface IAcpAgentSettingsCapability : IPluginCapability
{
    /// <summary>Reads the cached registry and local configuration without launching an agent.</summary>
    AcpAgentSettings GetAgentSettings();

    /// <summary>Saves the selected provider and the user's generation preference.</summary>
    void SaveAgentSettings(AcpAgentChoice? agent, bool enabled);
}

/// <summary>An agent displayed in the provider picker.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Executable">The detected local executable path, if available.</param>
/// <param name="Arguments">The arguments used to start the agent.</param>
public sealed record AcpAgentChoice(string Name, string? Executable, IReadOnlyList<string> Arguments)
{
    /// <summary>Whether a local executable was found.</summary>
    public bool IsInstalled => Executable is not null;
}

/// <summary>A snapshot of local ACP provider settings.</summary>
/// <param name="Agents">Agents available to browse.</param>
/// <param name="SelectedAgent">The current provider, if configured.</param>
/// <param name="Enabled">Whether AI description generation is enabled.</param>
/// <param name="RegistryStatus">An optional message about the registry cache.</param>
public sealed record AcpAgentSettings(IReadOnlyList<AcpAgentChoice> Agents,
    AcpAgentChoice? SelectedAgent, bool Enabled, string? RegistryStatus);
