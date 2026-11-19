using System;
using System.Linq;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed record RemoteLocation(
    string Scheme,
    string Host,
    int Port,
    string Path,
    string PublicUrl,
    RemoteTlsMode TlsMode = RemoteTlsMode.Off)
{
    public static RemoteLocation Parse(string? address, RemoteCatalogProviderRegistry? registry = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new RemoteConnectionException("ftp", LocalizationService.Current.Get(TextKeys.Address));
        }

        var trimmed = WithDefaultScheme(address.Trim());

        // The query is split off by hand: System.Uri folds a '?' into the path for ftp://
        // addresses, so uri.Query cannot be trusted to carry the tls= parameter (#623).
        var questionMark = trimmed.IndexOf('?');
        var query = questionMark < 0 ? string.Empty : trimmed[(questionMark + 1)..];
        var uriText = questionMark < 0 ? trimmed : trimmed[..questionMark];
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new RemoteConnectionException("ftp", SecretSanitizer.Sanitize(trimmed));
        }

        var provider = (registry ?? RemoteCatalogProviderRegistry.Default).Get(uri.Scheme);
        var port = uri.IsDefaultPort || uri.Port < 1 ? provider.DefaultPort : uri.Port;
        var path = provider.NormalizePath(Uri.UnescapeDataString(string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath));
        var tlsMode = ParseTlsQuery(query, trimmed) ?? provider.DefaultTlsMode;
        var builder = new UriBuilder(uri.Scheme, uri.IdnHost, port, EscapePath(path));
        var publicUrl = builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return new RemoteLocation(uri.Scheme, uri.IdnHost, port, path, publicUrl, tlsMode);
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

    internal static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string WithDefaultScheme(string address)
    {
        if (address.Contains("://", StringComparison.Ordinal))
        {
            return address;
        }

        // Windows UNC paths are SMB shares (#626); anything else without a scheme stays FTP.
        if (address.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // Each UNC component is escaped so literal '%', '#' and spaces survive the URI
            // round trip; the host itself is left as written. Supplied SMB URLs are untouched.
            var segments = address[2..].Split('\\');
            var escaped = new string[segments.Length];
            for (var i = 1; i < segments.Length; i++)
            {
                escaped[i] = Uri.EscapeDataString(segments[i]);
            }

            escaped[0] = segments[0];
            return "smb://" + string.Join("/", escaped);
        }

        return "ftp://" + address;
    }

    /// <summary>
    /// Reads the optional <c>tls=off|explicit|implicit</c> query parameter. Returns null when the
    /// parameter is absent so the provider's default wins; an unknown value is rejected like any
    /// other malformed address instead of being silently ignored.
    /// </summary>
    private static RemoteTlsMode? ParseTlsQuery(string query, string trimmed)
    {
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            if (!key.Equals("tls", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = separator < 0 ? string.Empty : Uri.UnescapeDataString(pair[(separator + 1)..]);
            return value.ToLowerInvariant() switch
            {
                "off" => RemoteTlsMode.Off,
                "explicit" => RemoteTlsMode.Explicit,
                "implicit" => RemoteTlsMode.Implicit,
                _ => throw new RemoteConnectionException("ftp", SecretSanitizer.Sanitize(trimmed)),
            };
        }

        return null;
    }

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
