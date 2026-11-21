using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>The catalog document is not the JSON array of source URLs the contract defines.</summary>
public sealed class MalformedPluginCatalogException(Uri address)
    : InvalidOperationException($"The plugin catalog '{address}' is not a JSON array of source URLs.")
{
    public Uri Address { get; } = address;
}
