using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>A remote plugin document could not be read because the network failed, the request timed out or
/// the server answered with a non-success status.</summary>
public sealed class PluginSourceUnavailableException(Uri address, Exception innerException)
    : InvalidOperationException($"The plugin source '{address}' is unavailable.", innerException)
{
    public Uri Address { get; } = address;
}
