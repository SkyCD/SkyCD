using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>Stable primitive types for versioned media metadata.</summary>
public enum MediaMetadataType
{
    /// <summary>Unicode text.</summary>
    Text,
    /// <summary>Signed 64-bit integer.</summary>
    Integer,
    /// <summary>Finite floating-point number.</summary>
    Number,
    /// <summary>True or false.</summary>
    Boolean,
    /// <summary>An instant with an explicit UTC offset.</summary>
    Timestamp
}

/// <summary>
/// Optional meaning beyond display. <see cref="PreferredName"/> lets the host show the value instead of the
/// catalogued file name, and <see cref="Tags"/> lets it turn the value into catalog tags. A provider only
/// publishes values for these roles while the matching user option is enabled; the host applies nothing
/// that was not published.
/// </summary>
public enum MediaMetadataFieldRole
{
    /// <summary>Display only; the host applies no catalog effect.</summary>
    None,
    /// <summary>A user-facing name that may replace the file name in the catalog browser.</summary>
    PreferredName,
    /// <summary>One or more tag names separated by semicolons or commas.</summary>
    Tags
}

/// <summary>Keys are stable within a provider and schema version; units use stable symbols such as s or px.</summary>
public sealed record MediaMetadataField(string Key, MediaMetadataType Type, string? Unit = null,
    MediaMetadataFieldRole Role = MediaMetadataFieldRole.None);
/// <summary>A stable provider-local schema identity and positive version.</summary>
public sealed record MediaMetadataSchema(string Id, int Version, IReadOnlyList<MediaMetadataField> Fields);
/// <summary>
/// Supported extensions/MIME types, output schema and descending selection priority. The <c>*</c> entry
/// matches any source. Opt-in providers are listed among an item's providers but are never executed by an
/// implicit extraction pass, so expensive whole-source work only runs on an explicit request.
/// <paramref name="IndexTimePreview"/> marks a preview the indexing pass also produces and stores with the
/// captured metadata, for artwork the catalog itself presents. Other previews wait for an explicit request.
/// </summary>
public sealed record MediaDescriptor(IReadOnlyList<string> Extensions, IReadOnlyList<string> ContentTypes,
    MediaMetadataSchema Schema, int Priority = 0, bool IsOptIn = false, bool IndexTimePreview = false);
/// <summary>Source facts without filesystem paths or persistence identifiers.</summary>
public sealed record MediaItem(string Extension, string? ContentType, long Length);

/// <summary>Only bounded bytes are exposed, never a filesystem path or persistence model.</summary>
public interface IMediaSource
{
    /// <summary>The source length in bytes.</summary>
    long Length { get; }
    /// <summary>Reads a range, charging its entire count against the cumulative source budget.</summary>
    ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken);
}

/// <summary>Operation completion fraction, from zero to one.</summary>
public sealed record MediaProgress(double Fraction);
/// <summary>A schema field value; exactly one payload must match the declared type.</summary>
public sealed record MediaMetadataValue(string Key, MediaMetadataType Type, string? Text = null,
    long? Integer = null, double? Number = null, bool? Boolean = null, DateTimeOffset? Timestamp = null);
/// <summary>Provider-local metadata; duplicate keys are rejected.</summary>
public sealed record MediaExtractionResult(IReadOnlyList<MediaMetadataValue> Values);
/// <summary>A data artifact described by MIME type, independent of UI and persistence.</summary>
public sealed record MediaPreviewArtifact(string ContentType, ReadOnlyMemory<byte> Data);
/// <summary>Generated preview artifacts subject to the aggregate output budget.</summary>
public sealed record MediaPreviewResult(IReadOnlyList<MediaPreviewArtifact> Artifacts);

/// <summary>Assembly-discovered media metadata extraction.</summary>
public interface IMediaMetadataCapability : IPluginCapability
{
    /// <summary>Supported source types and output schema.</summary>
    MediaDescriptor Descriptor { get; }
    /// <summary>Extracts typed data while observing cancellation and optional progress.</summary>
    Task<MediaExtractionResult> ExtractAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken);
}

/// <summary>Assembly-discovered media preview generation.</summary>
public interface IMediaPreviewCapability : IPluginCapability
{
    /// <summary>Supported source types and schema identity.</summary>
    MediaDescriptor Descriptor { get; }
    /// <summary>Produces data artifacts while observing cancellation and optional progress.</summary>
    Task<MediaPreviewResult> GeneratePreviewAsync(MediaItem item, IMediaSource source,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken);
}
