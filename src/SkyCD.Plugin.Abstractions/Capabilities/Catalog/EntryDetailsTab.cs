using System;

namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>
/// A per-entry tab a catalog feature plugin can embed in the host's Properties dialog. The host decides
/// where to render it; a headless host can accept the contribution without ever creating UI.
/// </summary>
public sealed class EntryDetailsTabContribution
{
    private readonly Func<EntryDetailsTab> createTab;

    /// <summary>Creates a contribution identified by <paramref name="key"/> and titled <paramref name="title"/>.</summary>
    public EntryDetailsTabContribution(string key, string title, int order, Func<EntryDetailsTab> createTab)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(createTab);
        Key = key;
        Title = title;
        Order = order;
        this.createTab = createTab;
    }

    /// <summary>Gets the stable key the host uses to select this tab.</summary>
    public string Key { get; }

    /// <summary>Gets the localized tab header.</summary>
    public string Title { get; }

    /// <summary>Gets the ordering hint used across contributors.</summary>
    public int Order { get; }

    /// <summary>Creates the tab instance; the host owns the returned instance and disposes it.</summary>
    public EntryDetailsTab CreateTab() => createTab();
}

/// <summary>
/// One instantiated per-entry tab. <see cref="Content"/> is created once on first access and is an
/// embeddable Avalonia control (never a Window); the host disposes the instance when the Properties
/// dialog closes.
/// </summary>
public sealed class EntryDetailsTab : IDisposable
{
    private readonly Func<object> createContent;
    private readonly Action? dispose;
    private object? content;
    private bool disposed;

    /// <summary>Creates a tab whose content is produced by <paramref name="createContent"/> on first access.</summary>
    public EntryDetailsTab(Func<object> createContent, Action? dispose = null)
    {
        ArgumentNullException.ThrowIfNull(createContent);
        this.createContent = createContent;
        this.dispose = dispose;
    }

    /// <summary>Gets the cached embeddable control rendered inside the tab.</summary>
    public object Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return content ??= createContent();
        }
    }

    /// <summary>Releases the tab content without returning it to the host.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var ownedContent = content;
        content = null;
        try
        {
            dispose?.Invoke();
        }
        finally
        {
            (ownedContent as IDisposable)?.Dispose();
        }
    }
}

/// <summary>
/// Raised when a feature plugin asks the host to open an entry's Properties dialog on one of its tabs.
/// </summary>
public sealed class EntryDetailsTabRequestEventArgs : EventArgs
{
    /// <summary>Creates a request for <paramref name="entryId"/> and <paramref name="tabKey"/>.</summary>
    public EntryDetailsTabRequestEventArgs(string entryId, string tabKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabKey);
        EntryId = entryId;
        TabKey = tabKey;
    }

    /// <summary>Gets the entry whose Properties dialog should open.</summary>
    public string EntryId { get; }

    /// <summary>Gets the contributing tab key that should be selected.</summary>
    public string TabKey { get; }
}
