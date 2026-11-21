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

    public bool IsCompatibleWith(Version hostVersion)
    {
        var min = Resolve(MinHostVersion) ?? DefaultMinHostVersion;
        var max = Resolve(MaxHostVersion);
        return hostVersion >= min && (max is null || hostVersion <= max);
    }

    private static readonly Version DefaultMinHostVersion = new(3, 0, 0);

    private static Version? Resolve(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Version.TryParse(value.Trim(), out var parsed) ? parsed : null;

    [JsonIgnore]
    public Version EffectiveMinHostVersion => Resolve(MinHostVersion) ?? DefaultMinHostVersion;

    [JsonIgnore]
    public Version? EffectiveMaxHostVersion => Resolve(MaxHostVersion);
}
