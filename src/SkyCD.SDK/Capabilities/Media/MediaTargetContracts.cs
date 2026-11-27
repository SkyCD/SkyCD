using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.SDK.Capabilities.Media;

/// <summary>
/// The kind of catalog medium a capture reads. Media in SkyCD are catalog roots, so a target is not
/// necessarily a mounted drive: it can be a folder, a single file, a network location, an optical disc or a
/// folder filled by a plugin source. A capability reports the kinds it reads through
/// <see cref="IMediaTargetMetadataCapability.Supports"/>.
/// </summary>
public enum MediaTargetKind
{
    /// <summary>A mounted volume with a filesystem, such as a fixed disk, a USB stick or a network-mapped drive.</summary>
    Volume,

    /// <summary>An optical drive, including discs that expose no filesystem, such as an audio CD.</summary>
    OpticalDisc,

    /// <summary>A local folder added as a media root.</summary>
    Folder,

    /// <summary>A single file added as a media root.</summary>
    File,

    /// <summary>A remote folder reached through a network connector, such as SMB, SFTP, FTP or WebDAV.</summary>
    NetworkFolder,

    /// <summary>A URL-addressed network resource added as a media root.</summary>
    NetworkResource,

    /// <summary>
    /// A catalog folder whose entries a plugin source produces or imports (an account catalog or a capture
    /// source factory), so the folder has no location of its own.
    /// </summary>
    SourceFolder
}

/// <summary>
/// One media root handed to a capture. Unlike <see cref="MediaItem"/> and <see cref="IMediaSource"/>, which
/// deliberately expose no filesystem paths, this contract carries the root because OS identity APIs, disc
/// readers and remote clients are addressed through it. Locations in this target must never be persisted or
/// shown by a plugin.
/// <paramref name="Location"/> is the address the root is opened through: a filesystem or device path, a UNC
/// path or a URL; it is null for a <see cref="MediaTargetKind.SourceFolder"/>, which has no address.
/// <paramref name="Name"/> is the catalog name of the root.
/// <paramref name="Facts"/> holds the facts the host already knows about the root, using the same keys as the
/// catalog entry properties (for example <c>VolumeLabel</c>, <c>DriveType</c> or <c>URL</c>), and may be empty.
/// <paramref name="Settings"/> holds the persisted settings of the plugin being asked, resolved by the host
/// per provider, because a capture may run outside any plugin configuration session.
/// </summary>
public sealed record MediaTarget(MediaTargetKind Kind, string? Location = null, string? Name = null,
    IReadOnlyDictionary<string, string?>? Facts = null,
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
/// Assembly-discovered metadata collection addressed by a media root. Implementations report which media
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

