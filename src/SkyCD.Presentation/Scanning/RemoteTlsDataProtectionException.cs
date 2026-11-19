using System;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteTlsDataProtectionException(string target, Exception? innerException = null)
    : RemoteTlsException(target, LocalizationService.Current.Format(TextKeys.RemoteTlsDataProtectionRejected, target), innerException)
{
}
