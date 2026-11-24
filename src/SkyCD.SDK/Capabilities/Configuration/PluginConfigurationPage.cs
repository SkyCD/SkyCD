using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Configuration;

/// <summary>
/// One Options-session draft. The desktop host accesses Content on its UI thread; headless hosts
/// can read the draft without creating UI. Neither creation nor acceptance may persist settings
/// or mutate running capabilities. The host owns persistence and disposes the page on close.
/// </summary>
public sealed class PluginConfigurationPage(
    Func<object> createContent,
    Func<CancellationToken, Task<PluginConfigurationResult>> acceptAsync,
    Action? dispose = null) : IDisposable
{
    private object? content;
    private bool disposed;

    /// <summary>Gets the cached embeddable Avalonia Control (never a Window) in the desktop host.</summary>
    public object Content
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return content ??= createContent();
        }
    }

    /// <summary>Validates and snapshots the draft without saving it. May be called again after a failed Save.</summary>
    public Task<PluginConfigurationResult> AcceptAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        return acceptAsync(cancellationToken);
    }

    /// <summary>Releases resources without accepting the draft.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var ownedContent = content;
        content = null;
        try { dispose?.Invoke(); }
        finally { (ownedContent as IDisposable)?.Dispose(); }
    }
}
