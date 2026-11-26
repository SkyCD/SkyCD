using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using SkyCD.SDK.Capabilities.Ui;
using SkyCD.UI.Dialogs;

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

    /// <inheritdoc />
    public async Task<QuestionResult> AskAsync(QuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await Dispatcher.UIThread.InvokeAsync(() =>
            ownerProvider() is { } owner
                ? QuestionDialog.AskAsync(owner, request)
                : Task.FromResult(QuestionResult.Cancel)).ConfigureAwait(false);
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
