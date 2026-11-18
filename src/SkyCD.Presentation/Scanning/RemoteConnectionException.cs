using System;
using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteConnectionException(string scheme, string target, Exception? innerException = null, string? detail = null)
    : IOException(FormatMessage(scheme, target, detail), innerException)
{
    private static string FormatMessage(string scheme, string target, string? detail)
    {
        var message = scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase)
            ? LocalizationService.Current.Format(TextKeys.FtpConnectionFailed, target)
            : LocalizationService.Current.Format(TextKeys.RemoteConnectionFailed, scheme.ToUpperInvariant(), target);
        return message + detail;
    }
}
