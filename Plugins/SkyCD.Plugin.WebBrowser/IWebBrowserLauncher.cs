namespace SkyCD.Plugin.WebBrowser;

public interface IWebBrowserLauncher
{
    Task OpenAsync(string url, CancellationToken cancellationToken = default);
}
