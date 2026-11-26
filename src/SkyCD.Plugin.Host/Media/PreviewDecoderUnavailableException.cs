using System;

namespace SkyCD.Plugin.Host.Media;

/// <summary>
/// An optional preview decoder is unavailable (undeployed native binding, unsupported platform).
/// Never thrown across the plugin boundary: the host publishes it as a once-per-plugin warning
/// and the entry outcome stays stored.
/// </summary>
public sealed class PreviewDecoderUnavailableException(string? reason) : InvalidOperationException(reason)
{
}
