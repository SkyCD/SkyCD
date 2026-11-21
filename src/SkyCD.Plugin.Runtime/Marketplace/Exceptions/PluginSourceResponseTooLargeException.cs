using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>A remote plugin document exceeded the configured response byte limit.</summary>
public sealed class PluginSourceResponseTooLargeException(Uri address, long maxBytes)
    : InvalidOperationException($"The response from '{address}' exceeded the {maxBytes} byte limit.")
{
    public Uri Address { get; } = address;

    public long MaxBytes { get; } = maxBytes;
}
