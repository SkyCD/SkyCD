using System;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemotePlatformNotSupportedException(string scheme) : NotSupportedException(
    LocalizationService.Current.Format(TextKeys.RemotePlatformNotSupported, scheme.ToUpperInvariant()))
{
}
