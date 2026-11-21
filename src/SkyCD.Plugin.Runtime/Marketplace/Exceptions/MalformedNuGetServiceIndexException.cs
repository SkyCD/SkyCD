using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>The feed's service index is not JSON or does not expose the package content resource.</summary>
public sealed class MalformedNuGetServiceIndexException(Uri address, string reason)
    : InvalidOperationException($"The NuGet service index '{address}' is malformed: {reason}")
{
    public Uri Address { get; } = address;

    public string Reason { get; } = reason;
}
