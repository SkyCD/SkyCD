using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;

namespace SkyCD.Plugin.Host.Volume;

public sealed record VolumeProvider(string PluginId, string PluginVersion, IVolumeMetadataCapability Capability);

/// <summary>
/// Runs only providers supplied by the existing enabled/compatible plugin pipeline. Capture is
/// best-effort: a failing provider is isolated and reported, and never breaks the surrounding scan.
/// </summary>
public sealed class VolumeExtensionManager : IVolumeMetadataProvider
{
    private readonly VolumeProvider[] providers;

    public VolumeExtensionManager(IEnumerable<VolumeProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
    }

    public bool HasVolumeProviders => providers.Length > 0;

    public IReadOnlyList<MediaMetadataValue> CaptureVolumeMetadata(VolumeMediaContext volume,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volume);

        var values = new List<MediaMetadataValue>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in OrderedProviders(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = provider.Capability.Extract(volume, cancellationToken);
                foreach (var value in result.Values)
                {
                    if (value is not null && seenKeys.Add(value.Key))
                    {
                        values.Add(value);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "volume", exception);
            }
        }

        return values;
    }

    private IEnumerable<VolumeProvider> OrderedProviders(CancellationToken cancellationToken)
    {
        var candidates = new List<(VolumeProvider Provider, int Priority)>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                candidates.Add((provider, provider.Capability.Descriptor.Priority));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "volume", exception);
            }
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Provider.PluginId, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Provider.Capability.GetType().FullName, StringComparer.Ordinal)
            .Select(static candidate => candidate.Provider);
    }
}
