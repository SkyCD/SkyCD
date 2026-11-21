using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>The feed's package version list is not JSON or not an array of version strings.</summary>
public sealed class MalformedNuGetVersionListException(Uri address)
    : InvalidOperationException($"The NuGet version list '{address}' is not a JSON array of version strings.")
{
    public Uri Address { get; } = address;
}
