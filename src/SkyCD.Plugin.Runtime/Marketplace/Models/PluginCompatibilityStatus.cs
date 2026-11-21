namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>How a package version relates to the running host.</summary>
public enum PluginCompatibilityStatus
{
    /// <summary>Selectable on this host.</summary>
    Compatible,

    /// <summary>Known to be incompatible; see <see cref="PluginCompatibilityReport.Reason"/>.</summary>
    Incompatible,

    /// <summary>Availability could not be confirmed, so the version is never selected automatically.</summary>
    Unknown
}
