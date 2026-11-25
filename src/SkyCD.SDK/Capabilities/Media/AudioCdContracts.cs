using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Media;

/// <summary>
/// Request for an audio CD capture. Compact Disc Digital Audio has no ordinary filesystem, so the plugin needs
/// the drive root the same way volume capture does; the host supplies the persisted settings because a capture
/// may run outside any plugin configuration session.
/// </summary>
public sealed record AudioCdDiscRequest(string RootPath, IReadOnlyDictionary<string, string?> Settings);

/// <summary>One track of a captured disc with its own values and optional cover.</summary>
public sealed record AudioCdTrackCapture(int Number, IReadOnlyList<MediaMetadataValue> Values,
    MediaPreviewArtifact? Cover = null);

/// <summary>
/// Disc and per-track results of one identified audio CD. The disc and track schemas describe the values so the
/// host can persist them durably and keep them readable offline; a capability that cannot identify the disc
/// returns <c>null</c> instead of an empty capture, which is what keeps unmatched and offline discs silent.
/// </summary>
public sealed record AudioCdCapture(
    MediaMetadataSchema DiscSchema,
    IReadOnlyList<MediaMetadataValue> DiscValues,
    MediaMetadataSchema TrackSchema,
    IReadOnlyList<AudioCdTrackCapture> Tracks,
    MediaPreviewArtifact? DiscCover = null);

/// <summary>
/// Assembly-discovered audio CD metadata collection. Implementations read the disc table of contents, query the
/// enabled online sources and honour their own persisted settings. They must return <c>null</c> for unsupported
/// platforms, unreadable drives, offline mode and unmatched discs, and must never throw for a network failure.
/// </summary>
public interface IAudioCdMetadataCapability : IPluginCapability
{
    /// <summary>Descending selection priority among competing audio CD providers.</summary>
    int Priority { get; }

    /// <summary>
    /// Extension, without a leading dot, naming the virtual track format. Audio CD capabilities have no
    /// file-format descriptor, so the host shows this with the plugin details instead.
    /// </summary>
    string? PrimaryExtension { get; }

    /// <summary>Identifies the disc and returns its metadata, or null when no disc could be identified.</summary>
    Task<AudioCdCapture?> CaptureAsync(AudioCdDiscRequest request, IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>A capture tagged with the provider that produced it, so the host can key cached results.</summary>
public sealed record AudioCdProviderCapture(
    string PluginId,
    string PluginVersion,
    string Capability,
    AudioCdCapture Capture);

/// <summary>
/// Host seam used by the media collection flow. Implemented over the enabled plugin capabilities and faked in
/// portable tests; providers must never break a scan when a capture fails.
/// </summary>
public interface IAudioCdMetadataProvider
{
    /// <summary>Gets whether any audio CD provider is registered and enabled.</summary>
    bool HasProviders { get; }

    /// <summary>Captures the identified disc, or null when no provider identifies it.</summary>
    Task<AudioCdProviderCapture?> CaptureAsync(string rootPath, CancellationToken cancellationToken);
}
