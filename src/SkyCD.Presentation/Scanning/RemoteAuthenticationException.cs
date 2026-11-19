using System;
using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteAuthenticationException(string scheme) : IOException(
    scheme.ToLowerInvariant() switch
    {
        "ftp" => LocalizationService.Current.Get(TextKeys.FtpAuthenticationFailed),
        "smb" => LocalizationService.Current.Get(TextKeys.SmbAuthenticationFailed),
        "webdav" or "webdavs" => LocalizationService.Current.Get(TextKeys.WebDavAuthenticationFailed),
        _ => LocalizationService.Current.Format(TextKeys.RemoteAuthenticationFailed, scheme.ToUpperInvariant()),
    })
{
}
