using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>The publisher manifest is not JSON or does not follow the manifest schema it declares.</summary>
public sealed class MalformedPublisherManifestException(Uri address, string reason)
    : InvalidOperationException($"The publisher manifest '{address}' is malformed: {reason}")
{
    public Uri Address { get; } = address;

    public string Reason { get; } = reason;
}
