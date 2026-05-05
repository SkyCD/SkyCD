using System;

namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Defines the minimum supported host version at assembly level.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class MinHostVersionAttribute(string version) : Attribute
{
    /// <summary>
    /// Gets the minimum host version value.
    /// </summary>
    public string Version { get; } = version;
}
