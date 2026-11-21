using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using SkyCD.Plugin.Abstractions.Capabilities.Ui;

namespace SkyCD.App.PluginHost;

/// <summary>
/// Shows plugin-provided content in a host-owned modal window. The owner is resolved at call time, and a
/// headless host (no window) completes without rendering so plugin callers stay host-agnostic.
/// </summary>
public sealed class AvaloniaHostDialogApi(Func<Window?> ownerProvider) : IHostDialogApi
{
    /// <inheritdoc />
    public async Task ShowAsync(HostWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var owner = ownerProvider();
        if (owner is null)
        {
            return;
        }

        await Dispatcher.UIThread
            .InvokeAsync(() => ShowCoreAsync(request, owner))
            .ConfigureAwait(false);
    }

    private static async Task ShowCoreAsync(HostWindowRequest request, Window owner)
    {
        var window = new Window
        {
            Title = request.Title,
            Width = request.Width,
            Height = request.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = request.CreateContent()
        };
        await window.ShowDialog(owner);
    }
}
