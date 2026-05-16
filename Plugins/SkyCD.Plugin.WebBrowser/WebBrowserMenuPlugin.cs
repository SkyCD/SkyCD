using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.WebBrowser;

public sealed class WebBrowserMenuPlugin : IMenuPluginCapability
{
    private const string CommandId = "webbrowser.open";
    private static readonly Uri DefaultUrl = new("about:blank", UriKind.Absolute);
    private readonly Func<Uri, CancellationToken, Task<bool>> _launchAsync;

    public WebBrowserMenuPlugin()
        : this(DefaultLaunchAsync)
    {
    }

    public WebBrowserMenuPlugin(Func<Uri, CancellationToken, Task<bool>> launchAsync)
    {
        _launchAsync = launchAsync ?? throw new ArgumentNullException(nameof(launchAsync));
    }

    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new(CommandId, "Web Browser", "Tools", Order: 120)
    ];

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (!commandId.Equals(CommandId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var (isValid, targetUrl, validationError) = ResolveTargetUrl(context);
        if (!isValid)
        {
            await NotifyAsync(context.HostApi, validationError, cancellationToken);
            return;
        }

        var launchSucceeded = await _launchAsync(targetUrl, cancellationToken);
        if (!launchSucceeded)
        {
            await NotifyAsync(context.HostApi, $"Failed to open URL '{targetUrl}'.", cancellationToken);
        }
    }

    internal static (bool IsValid, Uri TargetUrl, string ValidationError) ResolveTargetUrl(MenuCommandContext context)
    {
        var candidate = ResolveContextUrl(context.Properties);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return (true, DefaultUrl, string.Empty);
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsedUrl))
        {
            return (false, DefaultUrl, $"Invalid URL '{candidate}'.");
        }

        if (!IsAllowedScheme(parsedUrl.Scheme))
        {
            return (false, DefaultUrl,
                $"Unsupported URL scheme '{parsedUrl.Scheme}'. Allowed schemes: http, https, about.");
        }

        return (true, parsedUrl, string.Empty);
    }

    private static string? ResolveContextUrl(IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return null;
        }

        foreach (var (key, value) in properties)
        {
            if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    private static bool IsAllowedScheme(string scheme) =>
        scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || scheme.Equals("about", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> DefaultLaunchAsync(Uri targetUrl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dialog = new NativeWebDialog
            {
                Title = "Web Browser",
                Source = targetUrl
            };

            dialog.Show();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Task NotifyAsync(IHostCommandApi? hostApi, string message, CancellationToken cancellationToken)
    {
        if (hostApi is null)
        {
            return Task.CompletedTask;
        }

        return hostApi.NotifyAsync(message, cancellationToken);
    }
}
