using System;
using System.Reflection;

namespace SkyCD.Core.Versioning;

public sealed class HostVersionProvider
{
    public Version Current { get; } = ResolveVersion();

    private static Version ResolveVersion()
    {
        var entryVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        if (entryVersion is null)
        {
            throw new InvalidOperationException(
                "Unable to resolve host version because entry assembly version is unavailable.");
        }

        return Normalize(entryVersion);
    }

    private static Version Normalize(Version version)
    {
        var major = Math.Max(version.Major, 0);
        var minor = version.Minor < 0 ? 0 : version.Minor;
        var build = version.Build < 0 ? 0 : version.Build;
        return new Version(major, minor, build);
    }
}
