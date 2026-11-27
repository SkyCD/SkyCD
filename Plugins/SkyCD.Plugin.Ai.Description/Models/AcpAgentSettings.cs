using System.Collections.Generic;
using System.Linq;

namespace SkyCD.Plugin.Ai.Description.Models;

/// <summary>An agent displayed in the ACP provider picker.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Executable">The detected local executable path, if available.</param>
/// <param name="Arguments">The arguments used to start the agent.</param>
public sealed record AcpAgentChoice(string Name, string? Executable, IReadOnlyList<string> Arguments)
{
    /// <summary>Whether a local executable was found.</summary>
    public bool IsInstalled => Executable is not null;

    /// <summary>The version advertised by the registry.</summary>
    public string? RegistryVersion { get; init; }

    /// <summary>The agent summary advertised by the registry.</summary>
    public string? Description { get; init; }

    /// <summary>The identity used to install this registry entry.</summary>
    public string? RegistryId { get; init; }

    /// <summary>Whether this entry has an install method for the active platform.</summary>
    public bool CanInstall { get; init; }

    /// <summary>Whether SkyCD owns the installation.</summary>
    public bool IsManaged { get; init; }

    /// <summary>The installed version when recorded by SkyCD.</summary>
    public string? InstalledVersion { get; init; }

    /// <summary>Whether an update is available for a SkyCD installation.</summary>
    public bool CanUpdate { get; init; }

    /// <summary>The version shown on the agent card.</summary>
    public string? DisplayVersion => IsManaged ? InstalledVersion : RegistryVersion;

    /// <summary>The expected file name when locating a native installation manually.</summary>
    public string? ExpectedExecutable { get; init; }

    /// <summary>Whether a named native executable can be located manually.</summary>
    public bool CanLocateExecutable => !IsInstalled && !IsManaged && ExpectedExecutable is not null;

    /// <summary>Whether there is a launch or installation action for this platform.</summary>
    public bool HasAvailableAction => IsInstalled || IsManaged || CanInstall || CanLocateExecutable;

    /// <summary>Monochrome path data from the agent's registry icon, when available.</summary>
    public IReadOnlyList<string> IconPaths { get; init; } = [];

    /// <summary>A short visual identifier used when an agent has no rendered logo.</summary>
    public string IconLabel => string.Concat(Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
        .Take(2).Select(word => char.ToUpperInvariant(word[0])));
}

/// <summary>A snapshot of local ACP provider settings.</summary>
/// <param name="Agents">Agents available to browse.</param>
/// <param name="SelectedAgent">The current provider, if configured.</param>
/// <param name="RegistryStatus">An optional message about the registry cache.</param>
public sealed record AcpAgentSettings(IReadOnlyList<AcpAgentChoice> Agents,
    AcpAgentChoice? SelectedAgent, string? RegistryStatus)
{
    /// <summary>Whether the saved agent is enabled for generation.</summary>
    public bool IsGenerationEnabled { get; init; }
}
