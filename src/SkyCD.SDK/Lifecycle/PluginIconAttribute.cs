using System;

namespace SkyCD.Plugin.Abstractions.Lifecycle;

/// <summary>
/// Declares the icon the host shows for a plugin in Options → Plugins. The value is a Lucide glyph key such
/// as <c>Lucide:Archive</c>, an extension key such as <c>ext:json</c>, or a file path relative to the plugin
/// assembly (an <c>icon.png</c> beside the assembly is also picked up automatically). Without this
/// attribute the host falls back to the icon of the plugin's primary capability kind.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class PluginIconAttribute(string icon) : Attribute
{
    /// <summary>
    /// Gets the declared icon key.
    /// </summary>
    public string Icon { get; } = icon;
}
