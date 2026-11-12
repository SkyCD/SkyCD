using System;
using SkyCD.Presentation.Operations;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed record FtpLocation(string Host, int Port, string Path, string PublicUrl)
{
    public static FtpLocation Parse(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new FtpConnectionException(LocalizationService.Current.Format(
                TextKeys.FtpConnectionFailed,
                LocalizationService.Current.Get(TextKeys.Address)));
        }

        var trimmed = address.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "ftp://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new FtpConnectionException(LocalizationService.Current.Format(
                TextKeys.FtpConnectionFailed,
                SecretSanitizer.Sanitize(trimmed)));
        }

        if (!uri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase))
        {
            throw new FtpSchemeNotSupportedException(
                LocalizationService.Current.Get(TextKeys.OnlyFileTransferProtocolCurrentlyIsSupported));
        }

        var port = uri.IsDefaultPort || uri.Port < 1 ? 21 : uri.Port;
        var path = NormalizePath(Uri.UnescapeDataString(string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath));
        var builder = new UriBuilder(Uri.UriSchemeFtp, uri.IdnHost, port, path);
        var publicUrl = builder.Uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.UriEscaped);
        return new FtpLocation(uri.IdnHost, port, path, publicUrl);
    }

    public static bool TryReadUserInfo(string? address, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var trimmed = address.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "ftp://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
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

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new System.Collections.Generic.List<string>(parts.Length);
        foreach (var part in parts)
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (stack.Count > 0)
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                continue;
            }

            stack.Add(part);
        }

        return stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
    }

    public static string Combine(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return NormalizePath(directory);
        }

        if (name.StartsWith('/'))
        {
            return NormalizePath(name);
        }

        var parent = NormalizePath(directory);
        return parent == "/" ? "/" + name : parent + "/" + name;
    }
}
