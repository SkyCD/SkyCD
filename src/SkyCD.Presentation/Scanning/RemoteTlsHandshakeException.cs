using System;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteTlsHandshakeException(string target, Exception? innerException = null)
    : RemoteTlsException(target, LocalizationService.Current.Format(TextKeys.RemoteTlsHandshakeFailed, target), innerException)
{
}
