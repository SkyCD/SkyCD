using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Runtime.Marketplace.Models;

/// <summary>
/// Configuration of the plugin catalog service: endpoints, runtime compatibility inputs and the
/// bounds the service enforces on remote documents.
/// </summary>
public sealed record PluginCatalogOptions
{
    /// <summary>The default awesome-skycd catalog endpoint, served from GitHub Pages.</summary>
    public static readonly Uri DefaultCatalogEndpoint =
        new("https://skycd.github.io/awesome-skycd/plugins.json");

    /// <summary>
    /// The catalog location the app used before GitHub Pages. It is recognized only so a stale copy kept in a
    /// user repository list is dropped as a duplicate of the built-in catalog instead of being fetched as a
    /// publisher manifest.
    /// </summary>
    public static readonly Uri RetiredCatalogEndpoint =
        new("https://raw.githubusercontent.com/SkyCD/awesome-skycd/main/plugins.json");

    /// <summary>Catalog document locations, tried in order; the first one that answers is used.</summary>
    public IReadOnlyList<Uri> CatalogEndpoints { get; init; } = [DefaultCatalogEndpoint];

    /// <summary>Whether the address is a built-in catalog document rather than a publisher manifest.</summary>
    public static bool IsBuiltInCatalogEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri)
        {
            return false;
        }

        return IsSameEndpoint(endpoint, DefaultCatalogEndpoint) || IsSameEndpoint(endpoint, RetiredCatalogEndpoint);
    }

    /// <summary>Running SkyCD host version, compared with each release's host range.</summary>
    public Version HostVersion { get; init; } = new(0, 0, 0);

    /// <summary>Running runtime identifier, for example <c>win-x64</c>; the platform part decides package
    /// architecture eligibility.</summary>
    public string RuntimeIdentifier { get; init; } = CurrentRuntimeIdentifier();

    /// <summary>Whether prerelease package versions may be selected automatically.</summary>
    public bool IncludePrerelease { get; init; }

    /// <summary>Byte limit for the catalog document.</summary>
    public long MaxCatalogBytes { get; init; } = 1024 * 1024;

    /// <summary>Byte limit for one publisher manifest.</summary>
    public long MaxManifestBytes { get; init; } = 512 * 1024;

    /// <summary>Byte limit for a NuGet service index or package version list.</summary>
    public long MaxFeedDocumentBytes { get; init; } = 256 * 1024;

    /// <summary>Maximum number of publisher manifest requests in flight at once.</summary>
    public int MaxSourceConcurrency { get; init; } = 4;

    /// <summary>Maximum number of packages exposed from one publisher manifest.</summary>
    public int MaxPackagesPerManifest { get; init; } = 500;

    /// <summary>Maximum number of releases exposed per package.</summary>
    public int MaxReleasesPerPackage { get; init; } = 100;

    /// <summary>How long a resolved package stays reusable before the next browse triggers a network refresh.</summary>
    public TimeSpan CacheLifetime { get; init; } = TimeSpan.FromHours(6);

    /// <summary>Per-request timeout applied to every catalog, manifest and feed request.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The runtime identifier of the current process, for example <c>win-x64</c>.</summary>
    public static string CurrentRuntimeIdentifier()
    {
        var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm => "arm",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            var other => other.ToString().ToLowerInvariant()
        };

        return $"{PlatformOf(Environment.OSVersion.Platform)}-{architecture}";
    }

    private static string PlatformOf(PlatformID platform)
    {
        return platform switch
        {
            PlatformID.Win32NT or PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.WinCE => "win",
            PlatformID.MacOSX => "osx",
            _ => "linux"
        };
    }

    private static bool IsSameEndpoint(Uri left, Uri right)
    {
        return Uri.Compare(left, right, UriComponents.HttpRequestUrl, UriFormat.Unescaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }
}
