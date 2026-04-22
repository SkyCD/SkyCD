using Avalonia.Threading;

namespace SkyCD.Plugin.WebBrowser;

public sealed class AvaloniaWebBrowserLauncher : IWebBrowserLauncher
{
    public async Task OpenAsync(string url, CancellationToken cancellationToken = default)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = new PluginWebBrowserWindow(url);
            window.Show();
        });
    }
}
