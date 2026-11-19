using System;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteCertificateValidationException(string target, Exception? innerException = null)
    : RemoteTlsException(target, LocalizationService.Current.Format(TextKeys.RemoteCertificateValidationFailed, target), innerException)
{
}
