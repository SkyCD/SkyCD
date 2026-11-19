using System;
using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteListingNotSupportedException(string path, string stage, string scheme) : IOException(
    scheme.ToLowerInvariant() switch
    {
        "ftp" => LocalizationService.Current.Format(TextKeys.FtpListingNotSupported, path),
        "webdav" or "webdavs" => LocalizationService.Current.Format(TextKeys.WebDavListingNotSupported, path),
        _ => LocalizationService.Current.Format(TextKeys.RemoteListingNotSupported, scheme.ToUpperInvariant(), path),
    })
{
    public string Path { get; } = path;

    public string Stage { get; } = stage;
}
