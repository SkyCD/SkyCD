using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.WebBrowser;

public sealed class WebBrowserPlugin : IPlugin, IMenuPluginCapability
{
    private const string OpenCommandId = "webbrowser.open";
    private const string UrlProperty = "url";
    private const string DefaultUrlProperty = "defaultUrl";
    private readonly IWebBrowserLauncher _browserLauncher;

    public WebBrowserPlugin() : this(new AvaloniaWebBrowserLauncher())
    {
    }

    public WebBrowserPlugin(IWebBrowserLauncher browserLauncher)
    {
        _browserLauncher = browserLauncher;
    }

    public PluginDescriptor Descriptor { get; } = new(
        "SkyCD.Plugin.WebBrowser",
        "Web Browser",
        new Version(2, 0, 0),
        new Version(3, 0, 0));

    public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new MenuContribution(OpenCommandId, "Web Browser", "Tools", 100)
    ];

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context, CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals(OpenCommandId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (context.HostApi is null)
        {
            throw new InvalidOperationException("Host API is required.");
        }

        var url = ResolveUrl(context.Properties);
        if (!IsValidUrlScheme(url))
        {
            await context.HostApi.NotifyAsync("Invalid URL scheme. Allowed: http, https, about", cancellationToken);
            return;
        }

        try
        {
            await _browserLauncher.OpenAsync(url, cancellationToken);
        }
        catch (Exception exception)
        {
            await context.HostApi.NotifyAsync($"Failed to open URL: {exception.Message}", cancellationToken);
        }
    }

    internal static bool IsValidUrlScheme(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveUrl(IReadOnlyDictionary<string, string>? properties)
    {
        if (TryGetProperty(properties, UrlProperty, out var explicitUrl))
        {
            return explicitUrl;
        }

        if (TryGetProperty(properties, DefaultUrlProperty, out var fallbackUrl))
        {
            return fallbackUrl;
        }

        return "about:blank";
    }

    private static bool TryGetProperty(IReadOnlyDictionary<string, string>? properties, string key, out string value)
    {
        if (properties is not null
            && properties.TryGetValue(key, out var candidate)
            && !string.IsNullOrWhiteSpace(candidate))
        {
            value = candidate;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
