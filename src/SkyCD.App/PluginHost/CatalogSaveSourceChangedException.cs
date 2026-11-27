using System;

namespace SkyCD.App.PluginHost;

/// <summary>
/// The open catalog no longer holds an entry a plugin asked to save, so the write would silently drop it.
/// </summary>
public sealed class CatalogSaveSourceChangedException()
    : InvalidOperationException(
        "The open catalog changed while it was being written. Reopen it and try again.");
