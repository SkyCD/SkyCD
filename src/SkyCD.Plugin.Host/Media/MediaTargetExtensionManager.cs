using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

public sealed record MediaTargetProvider(string PluginId, string PluginVersion, IMediaTargetMetadataCapability Capability);

/// <summary>
/// Runs only providers supplied by the existing enabled/compatible plugin pipeline. Each provider receives
/// the settings persisted through its plugin configuration and is asked only for media it supports; a
/// failing provider is isolated and reported, and never breaks the surrounding scan.
/// </summary>
public sealed class MediaTargetExtensionManager : IMediaTargetMetadataProvider
{
    private static readonly IReadOnlyDictionary<string, string?> NoSettings =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private readonly MediaTargetProvider[] providers;
    private readonly Func<string, IReadOnlyDictionary<string, string?>>? settingsResolver;

    public MediaTargetExtensionManager(IEnumerable<MediaTargetProvider> providers,
        Func<string, IReadOnlyDictionary<string, string?>>? settingsResolver = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
        this.settingsResolver = settingsResolver;
    }

    public bool HasProviders => providers.Length > 0;

    public async Task<IReadOnlyList<MediaTargetProviderCapture>> CaptureAsync(MediaTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var captures = new List<MediaTargetProviderCapture>();
        foreach (var provider in OrderedProviders(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var providerTarget = target with { Settings = ResolveSettings(provider.PluginId) };
            try
            {
                if (!provider.Capability.Supports(providerTarget))
                {
                    continue;
                }

                var capture = await provider.Capability.CaptureAsync(providerTarget, progress: null, cancellationToken)
                    .ConfigureAwait(false);
                if (capture is not null)
                {
                    captures.Add(new MediaTargetProviderCapture(provider.PluginId, provider.PluginVersion,
                        provider.Capability.GetType().FullName ?? provider.PluginId, capture));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "media-target", exception);
            }
        }

        return captures;
    }

    private IReadOnlyDictionary<string, string?> ResolveSettings(string pluginId)
    {
        if (settingsResolver is null)
        {
            return NoSettings;
        }

        try
        {
            return settingsResolver(pluginId) ?? NoSettings;
        }
        catch (Exception exception)
        {
            // A configuration store problem must not stop the capture; the plugin's defaults still apply.
            PluginFailure.Publish(typeof(MediaTargetExtensionManager), "media-target-settings", exception);
            return NoSettings;
        }
    }

    private IEnumerable<MediaTargetProvider> OrderedProviders(CancellationToken cancellationToken)
    {
        var candidates = new List<(MediaTargetProvider Provider, int Priority)>();
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
                PluginFailure.Publish(provider.Capability.GetType(), "media-target", exception);
            }
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Provider.PluginId, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Provider.Capability.GetType().FullName, StringComparer.Ordinal)
            .Select(static candidate => candidate.Provider);
    }
}
