using System;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>
/// Facts about one source whose metadata is read through the operating system instead of its bytes.
/// Windows property stores, NTFS alternate data streams and extended attributes are addressed by path,
/// which <see cref="MediaItem"/> and <see cref="IMediaSource"/> deliberately do not expose. The path is
/// supplied only while the source is available locally and must never be persisted or shown to the user.
/// </summary>
public sealed record LocalMediaContext(string Path, string Extension, string? ContentType, long Length);

/// <summary>
/// Assembly-discovered metadata extraction addressed by a local path, for metadata the user or another
/// program assigned through the OS: Explorer tags, comments and ratings, or file-manager extended
/// attributes. Such values are not part of the file bytes and cannot be read through
/// <see cref="IMediaSource"/>.
/// </summary>
public interface ILocalMediaMetadataCapability : IPluginCapability
{
    /// <summary>Supported source types and output schema.</summary>
    MediaDescriptor Descriptor { get; }

    /// <summary>Extracts typed data with read-only OS access; empty when the platform or source has none.</summary>
    Task<MediaExtractionResult> ExtractAsync(LocalMediaContext source, IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Assembly-discovered preview generation addressed by a local path, for artwork that is stored beside the
/// source or elsewhere on its volume instead of inside the source bytes: a launcher entry's <c>Icon=</c>
/// file, a desktop theme image resolved from the volume, or a bundle icon stored in a sibling folder.
/// </summary>
/// <remarks>
/// Like <see cref="ILocalMediaMetadataCapability"/>, the path is host-owned: it is supplied only while the
/// source is available locally and must never be persisted, logged or published. Without a path the provider
/// contributes no result at all, so an offline entry keeps whatever preview the catalog already stored.
/// </remarks>
public interface ILocalMediaPreviewCapability : IPluginCapability
{
    /// <summary>Supported source types and schema identity.</summary>
    MediaDescriptor Descriptor { get; }

    /// <summary>Produces read-only artifacts for a locally available source.</summary>
    Task<MediaPreviewResult> GeneratePreviewAsync(LocalMediaContext source, IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken);
}
