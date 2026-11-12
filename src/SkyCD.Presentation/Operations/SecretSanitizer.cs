using System;
using System.Text.RegularExpressions;

namespace SkyCD.Presentation.Operations;

public static partial class SecretSanitizer
{
    [GeneratedRegex(@"\b(?<protocol>[a-zA-Z][a-zA-Z0-9+.-]*://)(?<user>[^:/\s]+):(?<password>[^@\s]+)@", RegexOptions.IgnoreCase)]
    private static partial Regex UriCredentialsRegex();

    [GeneratedRegex(@"(?<key>(?:password|pwd|secret|token|apikey|api_key|access_token|auth_token)\s*=\s*)(?<val>[^;\s&]+)", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValueSecretsRegex();

    [GeneratedRegex(@"(?<key>""?(?:password|pwd|secret|token|apikey|api_key|access_token|auth_token)""?\s*:\s*""?)(?<val>[^"",\s}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex JsonSecretsRegex();

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = UriCredentialsRegex().Replace(text, "${protocol}${user}:***@");
        result = KeyValueSecretsRegex().Replace(result, "${key}***");
        result = JsonSecretsRegex().Replace(result, "${key}***");
        return result;
    }
}
