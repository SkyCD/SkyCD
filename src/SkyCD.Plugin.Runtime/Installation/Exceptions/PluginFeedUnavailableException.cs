namespace SkyCD.Plugin.Runtime.Installation.Exceptions;

/// <summary>The NuGet service index is unreachable, insecure, malformed, or does not expose a package base address.</summary>
public sealed class PluginFeedUnavailableException(string serviceIndexUrl, string detail)
    : PluginInstallException(
        $"The package source '{serviceIndexUrl}' is unavailable or does not expose a package base address: {detail}.",
        PluginInstallFailureReason.FeedUnavailable);
