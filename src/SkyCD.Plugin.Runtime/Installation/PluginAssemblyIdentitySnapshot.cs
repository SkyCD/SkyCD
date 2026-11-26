using System;
using System.Text.Json.Serialization;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Plugin identity read from assembly metadata only; never produced by loading or executing plugin code.
/// </summary>
public sealed record PluginAssemblyIdentitySnapshot
{
    /// <summary>Plugin id from <c>PluginIdAttribute</c>, or the assembly simple name when the attribute is absent.</summary>
    public required string PluginId { get; init; }

    public required string AssemblyName { get; init; }

    /// <summary>Assembly identity version as a normalized string.</summary>
    public required string AssemblyVersion { get; init; }

    /// <summary><c>MinHostVersionAttribute</c> value, or <c>null</c> when absent (the host default applies).</summary>
    public string? MinHostVersion { get; init; }

    /// <summary><c>MaxHostVersionAttribute</c> value, or <c>null</c> when absent.</summary>
    public string? MaxHostVersion { get; init; }

    /// <summary>Package-relative path of the entry assembly (forward slashes).</summary>
    public required string EntryAssemblyPath { get; init; }

    public bool IsCompatibleWith(Version hostVersion) =>
        Discovery.PluginCompatibilityEvaluator.IsCompatibleWithBounds(hostVersion, MinHostVersion, MaxHostVersion);

    [JsonIgnore]
    public Version EffectiveMinHostVersion =>
        Discovery.PluginCompatibilityEvaluator.TryParseBound(MinHostVersion)
        ?? Discovery.PluginCompatibilityEvaluator.DefaultMinHostVersion;

    [JsonIgnore]
    public Version? EffectiveMaxHostVersion =>
        Discovery.PluginCompatibilityEvaluator.TryParseBound(MaxHostVersion);
}
