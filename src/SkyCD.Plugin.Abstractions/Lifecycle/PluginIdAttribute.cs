using System;

namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Defines an optional plugin identifier override at assembly level.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PluginIdAttribute(string id) : Attribute
{
    /// <summary>
    /// Gets the plugin identifier override.
    /// </summary>
    public string Id { get; } = id;
}
