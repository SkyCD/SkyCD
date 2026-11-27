using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Plugins;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Host.AudioCd;

public sealed record AudioCdProvider(string PluginId, string PluginVersion, IAudioCdMetadataCapability Capability);

/// <summary>
/// Runs only providers supplied by the existing enabled/compatible plugin pipeline. Each provider receives
/// the settings persisted through its plugin configuration, and a failing provider is isolated so it can
/// never break the surrounding scan.
/// </summary>
public sealed class AudioCdExtensionManager : IAudioCdMetadataProvider
{
    private static readonly IReadOnlyDictionary<string, string?> NoSettings =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    private readonly AudioCdProvider[] providers;
    private readonly Func<string, IReadOnlyDictionary<string, string?>>? settingsResolver;

    public AudioCdExtensionManager(IEnumerable<AudioCdProvider> providers,
        Func<string, IReadOnlyDictionary<string, string?>>? settingsResolver = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        this.providers = providers.ToArray();
        this.settingsResolver = settingsResolver;
    }

    public bool HasProviders => providers.Length > 0;

    public async Task<AudioCdProviderCapture?> CaptureAsync(string rootPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        foreach (var provider in OrderedProviders(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var settings = ResolveSettings(provider.PluginId);
                var capture = await provider.Capability.CaptureAsync(
                        new AudioCdDiscRequest(rootPath, settings), progress: null, cancellationToken)
                    .ConfigureAwait(false);
                if (capture is not null)
                {
                    return new AudioCdProviderCapture(provider.PluginId, provider.PluginVersion,
                        provider.Capability.GetType().FullName ?? provider.PluginId, capture);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "audio-cd", exception);
            }
        }

        return null;
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
            PluginFailure.Publish(typeof(AudioCdExtensionManager), "audio-cd-settings", exception);
            return NoSettings;
        }
    }

    private IEnumerable<AudioCdProvider> OrderedProviders(CancellationToken cancellationToken)
    {
        var candidates = new List<(AudioCdProvider Provider, int Priority)>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                candidates.Add((provider, provider.Capability.Priority));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "audio-cd", exception);
            }
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Priority)
            .ThenBy(static candidate => candidate.Provider.PluginId, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Provider.Capability.GetType().FullName, StringComparer.Ordinal)
            .Select(static candidate => candidate.Provider);
    }
}
