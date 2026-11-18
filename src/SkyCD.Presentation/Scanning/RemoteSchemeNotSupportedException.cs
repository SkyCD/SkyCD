using System;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteSchemeNotSupportedException(string scheme)
    : NotSupportedException(LocalizationService.Current.Format(TextKeys.RemoteSchemeNotSupported, scheme))
{
    public string Scheme { get; } = scheme;
}
