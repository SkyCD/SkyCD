using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Media;

/// <summary>
/// The medium a path-addressed capture reads. A volume is ordinary mounted media with a filesystem; an
/// optical disc may expose a filesystem or, like an audio CD, none at all. Both are addressed by a root
/// path because the operating system identifies the medium, not a file, by that path.
/// </summary>
public enum MediaTargetKind
{
    /// <summary>A mounted volume with a filesystem.</summary>
    Volume,

    /// <summary>An optical drive, including discs without a filesystem.</summary>
    OpticalDisc
}

/// <summary>
/// One media medium handed to a capture. Unlike <see cref="MediaItem"/> and <see cref="IMediaSource"/>, which
/// deliberately expose no filesystem paths, this contract carries the medium root because OS identity APIs
/// and disc readers are addressed by it. Paths in this target must never be persisted or shown by a plugin.
/// <paramref name="Settings"/> holds the persisted settings of the plugin being asked, resolved by the host
/// per provider, because a capture may run outside any plugin configuration session.
/// </summary>
public sealed record MediaTarget(MediaTargetKind Kind, string RootPath, string? Name = null,
    string? VolumeLabel = null, string? DriveFormat = null,
    IReadOnlyDictionary<string, string?>? Settings = null);

/// <summary>
/// Selection order and the virtual item extension a capability publishes. <paramref name="PrimaryExtension"/>,
/// without a leading dot, lets the host show the item format a medium produces (the <c>cda</c> track entries of
/// an audio CD); media capabilities have no file-format descriptor, so this is shown with the plugin details.
/// </summary>
public sealed record MediaTargetDescriptor(int Priority = 0, string? PrimaryExtension = null);

/// <summary>One numbered child of a medium, such as an audio track, with its own values and optional cover.</summary>
public sealed record MediaTargetPart(int Number, IReadOnlyList<MediaMetadataValue> Values,
    MediaPreviewArtifact? Cover = null);

/// <summary>
/// Metadata captured from one medium. <paramref name="Schema"/> and <paramref name="Values"/> describe the
/// medium itself; <paramref name="PartSchema"/> and <paramref name="Parts"/> describe its numbered children,
/// which are empty for media that have none. A capability that cannot identify the medium returns
/// <see langword="null"/> instead of an empty capture, which is what keeps unmatched and offline media silent.
/// </summary>
public sealed record MediaTargetCapture(MediaMetadataSchema Schema, IReadOnlyList<MediaMetadataValue> Values,
    MediaMetadataSchema? PartSchema = null, IReadOnlyList<MediaTargetPart>? Parts = null,
    MediaPreviewArtifact? Cover = null);

/// <summary>
/// Assembly-discovered metadata collection addressed by a medium root. Implementations report which media
/// kinds they handle through <see cref="Supports"/> and honour their own persisted settings. They must return
/// <see langword="null"/> for unsupported platforms, unreadable media and unmatched media, and must never throw
/// for a network failure.
/// </summary>
public interface IMediaTargetMetadataCapability : IPluginCapability
{
    /// <summary>Descending selection priority among competing capabilities.</summary>
    MediaTargetDescriptor Descriptor { get; }

    /// <summary>Reports whether this capability reads the given medium.</summary>
    bool Supports(MediaTarget target);

    /// <summary>Identifies the medium and returns its metadata, or null when it could not be identified.</summary>
    Task<MediaTargetCapture?> CaptureAsync(MediaTarget target, IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>A capture tagged with the provider that produced it, so the host can key cached results.</summary>
public sealed record MediaTargetProviderCapture(
    string PluginId,
    string PluginVersion,
    string Capability,
    MediaTargetCapture Capture);

/// <summary>
/// Host seam used by the scanning and media collection flows. Implemented over the enabled plugin
/// capabilities and faked in portable tests; providers must never break a scan when a capture fails.
/// Captures are returned in provider priority order; a caller that needs every contributing provider merges
/// them, and a caller that needs one identification (a disc) takes the first successful capture.
/// </summary>
public interface IMediaTargetMetadataProvider
{
    /// <summary>Gets whether any media target provider is registered and enabled.</summary>
    bool HasProviders { get; }

    /// <summary>Captures the medium through every supporting provider; empty when none identifies it.</summary>
    Task<IReadOnlyList<MediaTargetProviderCapture>> CaptureAsync(MediaTarget target,
        CancellationToken cancellationToken);
}
