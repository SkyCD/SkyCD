using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>A plugin source, manifest, feed or redirect used a scheme or host that is not allowed.</summary>
public sealed class PluginSourceProtocolException(Uri address)
    : InvalidOperationException($"Plugin source '{address}' must be an absolute HTTPS URL without credentials.")
{
    public Uri Address { get; } = address;
}
