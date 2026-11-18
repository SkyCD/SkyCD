using System;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed record RemoteLocation(string Scheme, string Host, int Port, string Path, string PublicUrl)
{
    public static RemoteLocation Parse(string? address, RemoteCatalogProviderRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new RemoteConnectionException("ftp", LocalizationService.Current.Get(TextKeys.Address));
        }

        var trimmed = WithDefaultScheme(address.Trim());
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new RemoteConnectionException("ftp", SecretSanitizer.Sanitize(trimmed));
        }

        var provider = (registry ?? RemoteCatalogProviderRegistry.Default).Get(uri.Scheme);
        var port = uri.IsDefaultPort || uri.Port < 1 ? provider.DefaultPort : uri.Port;
        var path = provider.NormalizePath(Uri.UnescapeDataString(string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath));
        var builder = new UriBuilder(uri.Scheme, uri.IdnHost, port, path);
        var publicUrl = builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return new RemoteLocation(uri.Scheme, uri.IdnHost, port, path, publicUrl);
    }

    public static bool TryReadUserInfo(string? address, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;
        if (string.IsNullOrWhiteSpace(address) ||
            !Uri.TryCreate(WithDefaultScheme(address.Trim()), UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var separator = uri.UserInfo.IndexOf(':');
        var userPart = separator < 0 ? uri.UserInfo : uri.UserInfo[..separator];
        var passwordPart = separator < 0 ? string.Empty : uri.UserInfo[(separator + 1)..];
        username = Unescape(userPart);
        password = Unescape(passwordPart);
        return username.Length > 0 || password.Length > 0;
    }

    private static string WithDefaultScheme(string address) =>
        address.Contains("://", StringComparison.Ordinal) ? address : "ftp://" + address;

    private static string Unescape(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
