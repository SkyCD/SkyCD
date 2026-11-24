using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Abstractions.Capabilities.Volume;

/// <summary>
/// Facts about one mounted volume for capture. Unlike <see cref="MediaItem"/> and <see cref="IMediaSource"/>,
/// which deliberately expose no filesystem paths, this contract carries the volume root because OS volume
/// identity APIs are addressed by root path; volume facts must never be forced through the file-byte
/// metadata contracts.
/// </summary>
public sealed record VolumeMediaContext(string RootPath, string Name, string? VolumeLabel = null,
    string? DriveFormat = null);

/// <summary>Output schema and descending selection priority for a volume metadata provider.</summary>
public sealed record VolumeDescriptor(MediaMetadataSchema Schema, int Priority = 0);

/// <summary>Provider-local volume metadata; duplicate keys are resolved by the host.</summary>
public sealed record VolumeExtractionResult(IReadOnlyList<MediaMetadataValue> Values);

/// <summary>
/// Assembly-discovered volume metadata extraction. Runs only when the host captures media info for a
/// volume; implementations must observe cancellation and must report absence by returning no value for a
/// field instead of fabricating a placeholder, so a genuinely reported zero is never confused with failure.
/// </summary>
public interface IVolumeMetadataCapability : IPluginCapability
{
    /// <summary>Supported volume facts and output schema.</summary>
    VolumeDescriptor Descriptor { get; }

    /// <summary>Captures typed volume facts with read-only OS access; empty for unsupported platforms or unavailable values.</summary>
    VolumeExtractionResult Extract(VolumeMediaContext volume, CancellationToken cancellationToken);
}

/// <summary>
/// Volume-aware host seam used by the disk capture flow. Implemented by the host over enabled plugin
/// capabilities and faked in portable tests; providers must not break scanning when a capture fails.
/// </summary>
public interface IVolumeMetadataProvider
{
    /// <summary>Captures volume facts from every enabled provider; empty when none is available.</summary>
    IReadOnlyList<MediaMetadataValue> CaptureVolumeMetadata(VolumeMediaContext volume,
        CancellationToken cancellationToken);
}
