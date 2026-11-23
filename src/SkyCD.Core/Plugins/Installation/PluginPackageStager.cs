using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.PackagePaths;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

internal sealed record PluginPackagePlan(
    string PluginId,
    PluginPackageManifest Manifest,
    PluginAssemblyIdentitySnapshot Entry,
    IReadOnlyList<string> ClaimedPluginIds,
    string EntryAssemblyPath);

/// <summary>
/// Downloads, extracts and validates one package into a caller-owned staging directory. Returns a plan; it never
/// writes into the plugin root and never loads plugin code.
/// </summary>
internal sealed class PluginPackageStager(HttpMessageHandler? httpHandler, Version hostVersion,
    TimeSpan downloadTimeout, long maxPackageBytes)
{
    public const int MaxClaimedPluginIds = 16;

    public Version HostVersion { get; } = hostVersion;

    /// <summary>Creates the run-time download client whose transport lifetime is owned by the caller.</summary>
    public PluginPackageDownloadClient CreateDownloadClient() =>
        new(httpHandler, downloadTimeout, maxPackageBytes);

    public async Task<PluginPackagePlan> PrepareAsync(PluginPackageRequest request, string operationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var packageId = request.NormalizedPackageId;
        var version = request.NormalizedVersion;
        if (string.IsNullOrWhiteSpace(packageId) || !PackageIdentity.IsExactVersion(version))
        {
            throw new PluginPackageIdentityMismatchException(packageId, version, packageId, version);
        }

        var packagePath = Path.Combine(operationDirectory, "package.nupkg");
        var contentDirectory = Path.Combine(operationDirectory, "content");
        using (var client = CreateDownloadClient())
        {
            await client.DownloadPackageAsync(request, packagePath, cancellationToken).ConfigureAwait(false);
        }

        var content = new PluginPackageInspector().Extract(packageId, packagePath, contentDirectory);
        if (!string.Equals(content.Manifest.PackageId, packageId, StringComparison.OrdinalIgnoreCase) ||
            PackageIdentity.CompareVersions(content.Manifest.Version, version) != 0)
        {
            throw new PluginPackageIdentityMismatchException(packageId, version, content.Manifest.PackageId,
                content.Manifest.Version);
        }

        var entryPath = ResolveEntryAssemblyPath(packageId, content);
        var entry = PluginAssemblyMetadataInspector.ReadEntryIdentity(packageId, contentDirectory, entryPath);
        EnsureCompatible(packageId, version, entry);

        var claimed = new List<string> { entry.PluginId };
        foreach (var identity in PluginAssemblyMetadataInspector.ReadAllIdentities(contentDirectory))
        {
            if (claimed.Count >= MaxClaimedPluginIds)
            {
                break;
            }

            if (!claimed.Contains(identity.PluginId, StringComparer.OrdinalIgnoreCase))
            {
                claimed.Add(identity.PluginId);
            }
        }

        var normalizedEntryPath = PackageEntryPath.TryNormalizeSafe(entryPath, out var normalized,
            PackageEntryPath.PackageRules)
            ? normalized
            : entryPath;
        return new PluginPackagePlan(entry.PluginId, content.Manifest, entry, claimed, normalizedEntryPath);
    }

    public void EnsureCompatible(string packageId, string version, PluginAssemblyIdentitySnapshot entry)
    {
        if (PluginCompatibilityEvaluator.IsCompatible(entry.EffectiveMinHostVersion, entry.EffectiveMaxHostVersion,
                HostVersion))
        {
            return;
        }

        throw new PluginHostIncompatibilityException(entry.PluginId, version, entry.MinHostVersion ?? "3.0.0",
            entry.MaxHostVersion, HostVersion.ToString());
    }

    private static string ResolveEntryAssemblyPath(string packageId, PluginPackageContent content)
    {
        var entryAssembly = content.Manifest.EntryAssembly;
        if (!string.IsNullOrWhiteSpace(entryAssembly))
        {
            if (!PackageEntryPath.TryNormalizeSafe(entryAssembly, out var declared, PackageEntryPath.PackageRules) ||
                !declared.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginUnsafeArchivePathException(packageId, entryAssembly);
            }

            var match = content.ExtractedFiles.FirstOrDefault(path =>
                PackageEntryPath.TryNormalizeSafe(path, out var normalizedPath, PackageEntryPath.PackageRules) &&
                string.Equals(normalizedPath, declared, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }

            var byName = content.ExtractedFiles
                .Where(path => string.Equals(Path.GetFileName(path), Path.GetFileName(declared),
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path.Count(static character => character == Path.DirectorySeparatorChar))
                .FirstOrDefault();

            return byName
                   ?? throw new PluginPackageAssemblyException(packageId,
                       $"the declared entry assembly '{entryAssembly}' is not in the package");
        }

        var candidates = content.ExtractedFiles
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Count(static character => character == Path.DirectorySeparatorChar))
            .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new PluginPackageAssemblyException(packageId, "the package contains no assemblies");
        }

        if (candidates.Count > 1)
        {
            throw new PluginPackageAssemblyException(packageId,
                "the manifest does not declare pluginEntryAssembly and the package contains several assemblies");
        }

        return candidates[0];
    }
}
