using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;

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
        this.providers = providers.Where(p => p.Capability is IMediaMetadataCapability
            or ILocalMediaMetadataCapability or IMediaPreviewCapability or ILocalMediaPreviewCapability).ToArray();
        this.limits = limits ?? new MediaExecutionLimits();
        this.settingsSource = settingsSource;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.SourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.ArtifactBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.Concurrency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.limits.TimeoutMilliseconds);
        slots = new SemaphoreSlim(this.limits.Concurrency);
    }

    public bool HasMetadataProviders => providers.Any(IsMetadataProvider);

    /// <summary>True when a preview would be produced by an indexing pass.</summary>
    public bool HasIndexingPreviewProviders => providers.Any(provider => provider.Capability switch
    {
        IMediaPreviewCapability preview => preview.Descriptor is { IsOptIn: false, IndexTimePreview: true },
        ILocalMediaPreviewCapability local => local.Descriptor is { IsOptIn: false, IndexTimePreview: true },
        _ => false
    });

    /// <summary>The factory must return a new readable stream; the host owns and disposes it.</summary>
    /// <summary>
    /// Runs matching providers. A per-call <paramref name="limits"/> override exists for explicit operations
    /// such as whole-file hashing, whose source budget and duration are set by the requested file rather than
    /// by the preview defaults. <paramref name="sourcePath"/> is the validated local path of the source while
    /// it is available; providers that read OS-assigned metadata or sibling artwork need it and are skipped
    /// without it.
    /// </summary>
    public async Task<IReadOnlyList<MediaProviderResult>> ProcessAsync(MediaItem item,
        Func<CancellationToken, Task<Stream>> openSource, IProgress<MediaProgress>? progress = null,
        CancellationToken cancellationToken = default, MediaExecutionLimits? limits = null,
        MediaProviderSelection selection = MediaProviderSelection.Standard, string? sourcePath = null)
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

            if (provider.Capability is IMediaPreviewAvailability { PreviewUnavailableReason: { } reason })
            {
                // The decoder is known to be missing before any work starts: one clearly named
                // warning per plugin (collapsed by fingerprint in the dialog) and no per-file
                // error spam, while the entry outcome stays stored (issue #1202).
                var itemName = sourcePath is null ? null : Path.GetFileName(sourcePath);
                PluginFailure.Publish(provider.Capability.GetType(), "media",
                    new PreviewDecoderUnavailableException(reason), provider.PluginId, reason, itemName,
                    isWarning: true);
                continue;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(effective.TimeoutMilliseconds);
            var operation = RunAsync(provider, item, openSource, progress, timeout.Token, effective, selection,
                sourcePath);

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
                var itemName = sourcePath is null ? null : Path.GetFileName(sourcePath);
                PluginFailure.Publish(provider.Capability.GetType(), "media", exception, provider.PluginId,
                    FirstLine(exception.Message), itemName);
                // Record the provider failure even for an optional preview. The cache treats a failed
                // refresh as "keep the previous capture", and callers tell a timed-out poster from a
                // stored one by this result. The dialog still collapses the published failure by
                // fingerprint, so a missing poster does not spam one error line per file.
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
        ILocalMediaMetadataCapability local => local.Descriptor,
        IMediaPreviewCapability preview => preview.Descriptor,
        ILocalMediaPreviewCapability localPreview => localPreview.Descriptor,
        _ => null
    };

    private static bool IsMetadataProvider(MediaProvider provider) =>
        provider.Capability is IMediaMetadataCapability or ILocalMediaMetadataCapability;

    private async Task<(int Priority, MediaProviderResult Result)?> RunAsync(MediaProvider provider,
        MediaItem item, Func<CancellationToken, Task<Stream>> openSource,
        IProgress<MediaProgress>? progress, CancellationToken token, MediaExecutionLimits limits,
        MediaProviderSelection selection, string? sourcePath)
    {
        await slots.WaitAsync(token);
        try
        {
            // Task.Run also guards synchronous plugin entry points. The work is queued without the
            // operation token so that, once a slot is taken, the worker is guaranteed to start; passing
            // the token would let a timeout that fires before the pool schedules the delegate cancel it
            // and silently skip the plugin. A cooperating plugin still observes the cancelled token and
            // stops, while an uncooperative one retains its slot and stream until it exits, preventing it
            // from bypassing the concurrency limit.
            return await Task.Run(async () =>
            {
                var descriptor = ResolveDescriptor(provider.Capability);
                if (descriptor is null || !Matches(descriptor, item)) return null;
                ValidateSchema(descriptor.Schema);
                MediaExtractionResult? extraction = null;
                MediaPreviewResult? artifacts = null;
                if (provider.Capability is ILocalMediaMetadataCapability or ILocalMediaPreviewCapability)
                {
                    // Path-addressed metadata and artwork are unavailable to a provider without a validated
                    // local source; reporting a failure would wrongly downgrade cached values.
                    if (string.IsNullOrWhiteSpace(sourcePath)) return null;
                    var context = new LocalMediaContext(sourcePath, item.Extension, item.ContentType, item.Length);
                    if (provider.Capability is ILocalMediaMetadataCapability localMetadata)
                    {
                        var raw = await localMetadata.ExtractAsync(context, progress, token);
                        extraction = ValidateExtraction(descriptor, raw, limits);
                    }
                    if (provider.Capability is ILocalMediaPreviewCapability localPreview && RunsPreview(selection, descriptor))
                    {
                        var raw = await localPreview.GeneratePreviewAsync(context, progress, token);
                        artifacts = ValidateArtifacts(raw, limits);
                    }
                }
                else
                {
                    await using var stream = await openSource(token);
                    var source = new BoundedSource(stream, item.Length, limits.SourceBytes, token);
                    if (provider.Capability is IMediaMetadataCapability metadataCapability)
                    {
                        var raw = await metadataCapability.ExtractAsync(item, source, progress, token);
                        extraction = ValidateExtraction(descriptor, raw, limits);
                    }
                    if (provider.Capability is IMediaPreviewCapability previewCapability && RunsPreview(selection, descriptor))
                    {
                        var raw = await previewCapability.GeneratePreviewAsync(item, source, progress, token);
                        artifacts = ValidateArtifacts(raw, limits);
                    }
                }
                return ((int Priority, MediaProviderResult Result)?)(descriptor.Priority,
                    new MediaProviderResult(provider.PluginId, provider.PluginVersion,
                        provider.Capability.GetType().FullName!,
                        descriptor.Schema with { Fields = descriptor.Schema.Fields.ToArray() }, extraction, artifacts, false, false));
            });
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
                (IsMetadataProvider(provider) ||
                 ((provider.Capability is IMediaPreviewCapability or ILocalMediaPreviewCapability) &&
                  descriptor.IndexTimePreview)),
            MediaProviderSelection.OptInMetadata => IsMetadataProvider(provider) && descriptor.IsOptIn,
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

    private static MediaExtractionResult ValidateExtraction(MediaDescriptor descriptor, MediaExtractionResult raw,
        MediaExecutionLimits limits)
    {
        var extraction = new MediaExtractionResult(raw.Values.ToArray());
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

        return extraction;
    }

    /// <summary>Copies preview artifacts so a provider cannot mutate them after returning, within the output bound.</summary>
    private static MediaPreviewResult ValidateArtifacts(MediaPreviewResult raw, MediaExecutionLimits limits)
    {
        long bytes = 0;
        var copies = new List<MediaPreviewArtifact>();
        foreach (var artifact in raw.Artifacts)
        {
            bytes = checked(bytes + artifact.Data.Length);
            if (bytes > limits.ArtifactBytes || string.IsNullOrWhiteSpace(artifact.ContentType))
                throw new MediaLimitExceededException("artifact bytes");
            copies.Add(new MediaPreviewArtifact(artifact.ContentType, artifact.Data.ToArray()));
        }

        return new MediaPreviewResult(copies.ToArray());
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

    private static string FirstLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var newline = value.IndexOf('\n');
        if (newline >= 0)
        {
            value = value.Substring(0, newline);
        }

        return value.TrimEnd('\r').Trim();
    }

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
