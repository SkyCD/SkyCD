using System;

namespace SkyCD.Plugin.Runtime.Marketplace.Exceptions;

/// <summary>A version string is not a valid NuGet version.</summary>
public sealed class InvalidPluginPackageVersionException(string version)
    : FormatException($"'{version}' is not a valid plugin package version.")
{
    public string Version { get; } = version;
}
