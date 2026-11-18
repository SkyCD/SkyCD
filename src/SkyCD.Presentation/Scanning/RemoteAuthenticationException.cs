using System;
using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteAuthenticationException(string scheme) : IOException(
    scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase)
        ? LocalizationService.Current.Get(TextKeys.FtpAuthenticationFailed)
        : LocalizationService.Current.Format(TextKeys.RemoteAuthenticationFailed, scheme.ToUpperInvariant()))
{
}
