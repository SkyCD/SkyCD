using System;

namespace SkyCD.Presentation.Scanning;

internal static class FtpCredentials
{
    public static bool IsAnonymousUserName(string? username) =>
        username is not null &&
        (username.Equals("anonymous", StringComparison.OrdinalIgnoreCase) ||
         username.Equals("ftp", StringComparison.OrdinalIgnoreCase));

    public static (string Username, string Password) Resolve(string? username, string? password, bool anonymous)
    {
        if (anonymous || string.IsNullOrWhiteSpace(username))
        {
            return ("anonymous", "anonymous@");
        }

        var trimmedUser = username.Trim();
        if (IsAnonymousUserName(trimmedUser) && string.IsNullOrEmpty(password))
        {
            return (trimmedUser, "anonymous@");
        }

        return (trimmedUser, password ?? string.Empty);
    }
}
