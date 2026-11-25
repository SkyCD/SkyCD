using System;
using SkyCD.SDK.Capabilities.Catalog;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Host view model for one plugin-contributed per-entry tab inside the Properties dialog. The host creates
/// the tab once and disposes it with the dialog; the plugin-owned content is created on first access.
/// </summary>
public sealed class PropertiesDetailsTabViewModel(string key, string title, EntryDetailsTab tab) : IDisposable
{
    /// <summary>Gets the stable contribution key used to select the tab.</summary>
    public string Key { get; } = key;

    /// <summary>Gets the localized tab header.</summary>
    public string Title { get; } = title;

    /// <summary>Gets the embeddable control rendered inside the tab.</summary>
    public object Content => tab.Content;

    /// <summary>Releases the plugin-owned tab content.</summary>
    public void Dispose() => tab.Dispose();
}
