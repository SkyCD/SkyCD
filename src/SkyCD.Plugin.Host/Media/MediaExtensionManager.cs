using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Host.Media;

public sealed record MediaProvider(string PluginId, string PluginVersion, IPluginCapability Capability);

/// <summary>Runs only providers supplied by the existing enabled/compatible plugin pipeline.</summary>
public sealed class MediaExtensionManager
{
    private readonly MediaProvider[] providers;
    private readonly MediaExecutionLimits limits;
    private readonly Func<string, IReadOnlyDictionary<string, string?>>? settingsSource;
    private readonly SemaphoreSlim slots;

    public MediaExtensionManager(IEnumerable<MediaProvider> providers, MediaExecutionLimits? limits = null,
        Func<string, IReadOnlyDictionary<string, string?>>? settingsSource = null)
    {
        this.providers = providers.Where(p => p.Capability is IMediaMetadataCapability or IMediaPreviewCapability).ToArray();
        this.limits = limits ?? new MediaExecutionLimits();
        this.settingsSource = settingsSource;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.SourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.ArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.Concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.TimeoutMilliseconds);
        slots = new SemaphoreSlim(this.limits.Concurrency);
    }

    public bool HasMetadataProviders => providers.Any(provider =>
        provider.Capability is IMediaMetadataCapability);

    /// <summary>True when a preview would be produced by an indexing pass.</summary>
    public bool HasIndexingPreviewProviders => providers.Any(provider =>
        provider.Capability is IMediaPreviewCapability preview &&
        preview.Descriptor is { IsOptIn: false, IndexTimePreview: true });

    /// <summary>The factory must return a new readable stream; the host owns and disposes it.</summary>
    /// <summary>
    /// Runs matching providers. A per-call <paramref name="limits"/> override exists for explicit operations
    /// such as whole-file hashing, whose source budget and duration are set by the requested file rather than
    /// by the preview defaults.
    /// </summary>
    public async Task<IReadOnlyList<MediaProviderResult>> ProcessAsync(MediaItem item,
        Func<CancellationToken, Task<Stream>> openSource, IProgress<MediaProgress>? progress = null,
        CancellationToken cancellationToken = default, MediaExecutionLimits? limits = null,
        MediaProviderSelection selection = MediaProviderSelection.Standard)
    {
        var effective = limits ?? this.limits;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effective.SourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effective.ArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effective.TimeoutMilliseconds);
        ApplySettings();

        var results = new List<(int Priority, MediaProviderResult Result)>();
        foreach (var provider in OrderedProviders())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSelected(provider, item, selection))
            {
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(effective.TimeoutMilliseconds);
            var operation = RunAsync(provider, item, openSource, progress, timeout.Token, effective, selection);

            try
            {
                var result = await operation.WaitAsync(timeout.Token);
                if (result.HasValue) results.Add(result.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Observe(operation);
                throw;
            }
            catch (Exception exception)
            {
                Observe(operation);
                PluginFailure.Publish(provider.Capability.GetType(), "media", exception);
                results.Add((int.MinValue, Failure(provider, timeout.IsCancellationRequested)));
            }
        }

        return results.OrderByDescending(r => r.Priority).Select(r => r.Result).ToArray();
    }

    /// <summary>
    /// Identities of the registered providers that would run for the source, in the same deterministic
    /// order <see cref="ProcessAsync"/> uses. Lets a host decide whether cached results are still current
    /// without executing any plugin.
    /// </summary>
    public IReadOnlyList<MediaProviderIdentity> ResolveProviders(MediaItem item,
        MediaProviderSelection selection = MediaProviderSelection.All)
    {
        ArgumentNullException.ThrowIfNull(item);

        var revisions = SettingsRevisions(ApplySettings());
        var identities = new List<MediaProviderIdentity>();
        foreach (var provider in OrderedProviders())
        {
            var descriptor = ResolveDescriptor(provider.Capability);
            if (descriptor is null || !IsSelected(provider, item, selection))
            {
                continue;
            }

            identities.Add(new MediaProviderIdentity(
                provider.PluginId,
                provider.PluginVersion,
                provider.Capability.GetType().FullName!,
                descriptor.Schema.Id,
                descriptor.Schema.Version,
                descriptor.Priority,
                descriptor.Schema.Fields,
                revisions.TryGetValue(provider.PluginId, out var revision) ? revision : string.Empty));
        }

        return identities;
    }

    /// <summary>
    /// Pushes the persisted settings of each plugin to its capability before descriptors are resolved, so a
    /// configured extension list or an enabled/disabled option is visible to matching and extraction alike.
    /// Returns the settings that were pushed, keyed by plugin.
    /// </summary>
    private Dictionary<string, IReadOnlyDictionary<string, string?>> ApplySettings()
    {
        var byPlugin = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.OrdinalIgnoreCase);
        if (settingsSource is null)
        {
            return byPlugin;
        }

        foreach (var provider in providers)
        {
            if (provider.Capability is not IPluginSettingsConsumer consumer)
            {
                continue;
            }

            try
            {
                if (!byPlugin.TryGetValue(provider.PluginId, out var settings))
                {
                    settings = settingsSource(provider.PluginId)
                               ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    byPlugin[provider.PluginId] = settings;
                }

                consumer.ApplySettings(settings);
            }
            catch (Exception exception)
            {
                PluginFailure.Publish(provider.Capability.GetType(), "media-settings", exception);
            }
        }

        return byPlugin;
    }

    /// <summary>
    /// Stable per-plugin digest of the persisted settings; empty when none are stored. Providers report it in
    /// their identity, so changing an option makes earlier cached results stale like a schema change does.
    /// </summary>
    private static Dictionary<string, string> SettingsRevisions(
        Dictionary<string, IReadOnlyDictionary<string, string?>> settingsByPlugin)
    {
        var revisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, settings) in settingsByPlugin)
        {
            if (settings.Count == 0)
            {
                continue;
            }

            var canonical = string.Join('\n', settings
                .Where(setting => !string.IsNullOrWhiteSpace(setting.Key))
                .OrderBy(setting => setting.Key, StringComparer.Ordinal)
                .Select(setting => setting.Key + '=' + setting.Value));
            revisions[pluginId] = Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(canonical)))
                .ToLowerInvariant();
        }

        return revisions;
    }

    private IEnumerable<MediaProvider> OrderedProviders() => providers
        .OrderBy(p => p.PluginId, StringComparer.Ordinal)
        .ThenBy(p => p.Capability.GetType().FullName, StringComparer.Ordinal);

    private static MediaDescriptor? ResolveDescriptor(IPluginCapability capability) => capability switch
    {
        IMediaMetadataCapability metadata => metadata.Descriptor,
        IMediaPreviewCapability preview => preview.Descriptor,
        _ => null
    };

    private async Task<(int Priority, MediaProviderResult Result)?> RunAsync(MediaProvider provider,
        MediaItem item, Func<CancellationToken, Task<Stream>> openSource,
        IProgress<MediaProgress>? progress, CancellationToken token, MediaExecutionLimits limits,
        MediaProviderSelection selection)
    {
        await slots.WaitAsync(token);
        try
        {
            // Task.Run also guards synchronous plugin entry points. A timed-out worker retains its slot
            // and stream until it exits, preventing uncooperative plugins from bypassing concurrency limits.
            return await Task.Run(async () =>
            {
                var descriptor = ResolveDescriptor(provider.Capability);
                if (descriptor is null || !Matches(descriptor, item)) return null;
                ValidateSchema(descriptor.Schema);
                await using var stream = await openSource(token);
                var source = new BoundedSource(stream, item.Length, limits.SourceBytes, token);
                MediaExtractionResult? extraction = null;
                MediaPreviewResult? artifacts = null;
                if (provider.Capability is IMediaMetadataCapability metadataCapability)
                {
                    var raw = await metadataCapability.ExtractAsync(item, source, progress, token);
                    extraction = new MediaExtractionResult(raw.Values.ToArray());
                    if (extraction.Values.Sum(v => (long)Encoding.UTF8.GetByteCount(v.Text ?? string.Empty) + Encoding.UTF8.GetByteCount(v.Key) + 32) > limits.ArtifactBytes)
                        throw new MediaLimitExceededException("metadata bytes");
                    if (extraction.Values.Select(v => v.Key).Distinct(StringComparer.Ordinal).Count() != extraction.Values.Count)
                        throw new MediaLimitExceededException("duplicate metadata key");
                    foreach (var value in extraction.Values)
                    {
                        var field = descriptor.Schema.Fields.SingleOrDefault(f => f.Key == value.Key);
                        if (field is null || field.Type != value.Type || !ValidValue(value))
                            throw new MediaLimitExceededException("metadata schema");
                    }
                }
                if (provider.Capability is IMediaPreviewCapability previewCapability && RunsPreview(selection, descriptor))
                {
                    var raw = await previewCapability.GeneratePreviewAsync(item, source, progress, token);
                    long bytes = 0;
                    var copies = new List<MediaPreviewArtifact>();
                    foreach (var artifact in raw.Artifacts)
                    {
                        bytes = checked(bytes + artifact.Data.Length);
                        if (bytes > limits.ArtifactBytes || string.IsNullOrWhiteSpace(artifact.ContentType))
                            throw new MediaLimitExceededException("artifact bytes");
                        copies.Add(new MediaPreviewArtifact(artifact.ContentType, artifact.Data.ToArray()));
                    }
                    artifacts = new MediaPreviewResult(copies.ToArray());
                }
                return ((int Priority, MediaProviderResult Result)?)(descriptor.Priority,
                    new MediaProviderResult(provider.PluginId, provider.PluginVersion,
                        provider.Capability.GetType().FullName!,
                        descriptor.Schema with { Fields = descriptor.Schema.Fields.ToArray() }, extraction, artifacts, false, false));
            }, token);
        }
        finally { slots.Release(); }
    }

    private static bool Matches(MediaDescriptor descriptor, MediaItem item) =>
        descriptor.Extensions.Contains("*", StringComparer.Ordinal) ||
        descriptor.ContentTypes.Contains("*", StringComparer.Ordinal) ||
        descriptor.Extensions.Contains(item.Extension, StringComparer.OrdinalIgnoreCase) ||
        (item.ContentType is not null && descriptor.ContentTypes.Contains(item.ContentType, StringComparer.OrdinalIgnoreCase));

    private static bool IsSelected(MediaProvider provider, MediaItem item, MediaProviderSelection selection)
    {
        var descriptor = ResolveDescriptor(provider.Capability);
        if (descriptor is null || !Matches(descriptor, item))
        {
            return false;
        }

        return selection switch
        {
            MediaProviderSelection.Standard => !descriptor.IsOptIn,
            MediaProviderSelection.OptInOnly => descriptor.IsOptIn,
            MediaProviderSelection.StandardMetadata => !descriptor.IsOptIn &&
                (provider.Capability is IMediaMetadataCapability ||
                 (provider.Capability is IMediaPreviewCapability && descriptor.IndexTimePreview)),
            MediaProviderSelection.OptInMetadata => provider.Capability is IMediaMetadataCapability && descriptor.IsOptIn,
            _ => true
        };
    }

    /// <summary>
    /// Whether this pass may generate the provider's preview. A metadata-only pass never runs previews it
    /// was not explicitly made responsible for; indexing runs only providers that opted into indexing.
    /// </summary>
    private static bool RunsPreview(MediaProviderSelection selection, MediaDescriptor descriptor) => selection switch
    {
        MediaProviderSelection.OptInMetadata => false,
        MediaProviderSelection.StandardMetadata => descriptor is { IsOptIn: false, IndexTimePreview: true },
        _ => true
    };

    private static void ValidateSchema(MediaMetadataSchema schema)
    {
        if (string.IsNullOrWhiteSpace(schema.Id) || schema.Version < 1 ||
            schema.Fields.Any(f => string.IsNullOrWhiteSpace(f.Key) || !Enum.IsDefined(f.Type)) ||
            schema.Fields.Select(f => f.Key).Distinct(StringComparer.Ordinal).Count() != schema.Fields.Count)
            throw new MediaLimitExceededException("metadata schema");
    }

    private static bool ValidValue(MediaMetadataValue value) =>
        (value.Text is null ? 0 : 1) + (value.Integer.HasValue ? 1 : 0) + (value.Number.HasValue ? 1 : 0) +
        (value.Boolean.HasValue ? 1 : 0) + (value.Timestamp.HasValue ? 1 : 0) == 1 && value.Type switch
        {
            MediaMetadataType.Text => value.Text is not null,
            MediaMetadataType.Integer => value.Integer.HasValue,
            MediaMetadataType.Number => value.Number.HasValue && double.IsFinite(value.Number.Value),
            MediaMetadataType.Boolean => value.Boolean.HasValue,
            MediaMetadataType.Timestamp => value.Timestamp.HasValue,
            _ => false
        };

    private static MediaProviderResult Failure(MediaProvider provider, bool timedOut) =>
        new(provider.PluginId, provider.PluginVersion, provider.Capability.GetType().FullName!, null, null, null, true, timedOut);

    private static void Observe(Task task) => _ = task.ContinueWith(t => _ = t.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private sealed class BoundedSource(Stream stream, long length, long budget, CancellationToken operationToken) : IMediaSource
    {
        private readonly SemaphoreSlim reader = new(1);
        private long remaining = budget;
        private long position;
        public long Length => length;

        public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationToken);
            cancellationToken = linked.Token;
            await reader.WaitAsync(cancellationToken);
            try
            {
                if (offset < 0 || count < 0 || offset > Length || count > Length - offset || count > remaining)
                    throw new MediaLimitExceededException("source bytes");
                remaining -= count;
                if (stream.CanSeek) stream.Position = offset;
                else if (offset != position) throw new MediaLimitExceededException("source seek");
                var bytes = new byte[count];
                await stream.ReadExactlyAsync(bytes, cancellationToken);
                position = offset + count;
                return bytes;
            }
            finally { reader.Release(); }
        }
    }
}
