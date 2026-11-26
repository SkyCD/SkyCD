using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using SkyCD.SDK.Capabilities.Ui;

namespace SkyCD.App.PluginHost;

/// <summary>Clipboard access through the host's top-level window.</summary>
public sealed class AvaloniaClipboardApi(Func<Window?> ownerProvider) : IClipboardApi
{
    /// <inheritdoc />
    public async Task SetTextAsync(string text)
    {
        var topLevel = ResolveTopLevel();
        if (topLevel?.Clipboard is not { } clipboard)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => clipboard.SetTextAsync(text));
    }

    private TopLevel? ResolveTopLevel()
    {
        var owner = ownerProvider()
            ?? (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        return owner is null ? null : TopLevel.GetTopLevel(owner);
    }
}
