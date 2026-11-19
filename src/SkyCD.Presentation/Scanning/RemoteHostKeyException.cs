using System;
using System.IO;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.Scanning;

public sealed class RemoteHostKeyException(string target, string fingerprint, bool changed) : IOException(
    changed
        ? LocalizationService.Current.Format(TextKeys.RemoteHostKeyChanged, target, fingerprint)
        : LocalizationService.Current.Format(TextKeys.RemoteHostKeyUnknown, target, fingerprint))
{
    public string Target { get; } = target;

    public string Fingerprint { get; } = fingerprint;

    public bool Changed { get; } = changed;
}
