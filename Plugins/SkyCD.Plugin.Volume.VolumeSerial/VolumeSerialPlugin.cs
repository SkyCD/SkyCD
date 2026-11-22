using System;
using System.Collections.Generic;
using System.Threading;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Volume;
using SkyCD.Plugin.Volume.VolumeSerial.Models;
using SkyCD.Plugin.Volume.VolumeSerial.Sources;

namespace SkyCD.Plugin.Volume.VolumeSerial;

/// <summary>
/// Captures the filesystem volume serial of a disk while the host captures media info. Absence is
/// reported by returning no value, so a genuinely reported zero serial is never stored as a failure
/// and a failure never fabricates a placeholder. The plugin degrades to a no-op on unsupported platforms.
/// </summary>
public sealed class VolumeSerialPlugin : IVolumeMetadataCapability
{
    private static readonly VolumeExtractionResult NoValue = new(Array.Empty<MediaMetadataValue>());

    private readonly IVolumeSerialApi api;

    public VolumeSerialPlugin()
        : this(CreateDefaultApi())
    {
    }

    internal VolumeSerialPlugin(IVolumeSerialApi api)
    {
        this.api = api;
    }

    public VolumeDescriptor Descriptor => new(VolumeSerialSchema.Metadata);

    public VolumeExtractionResult Extract(VolumeMediaContext volume, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(volume);
        cancellationToken.ThrowIfCancellationRequested();
        if (!api.IsSupported || !api.TryGetVolumeSerial(volume.RootPath, out var volumeSerial))
        {
            return NoValue;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new VolumeExtractionResult(new List<MediaMetadataValue>(1)
        {
            new(VolumeSerialSchema.VolumeSerialNumber, MediaMetadataType.Text, Text: VolumeSerialSchema.Format(volumeSerial))
        });
    }

    private static IVolumeSerialApi CreateDefaultApi()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsVolumeSerialApi();
        }

        return UnsupportedVolumeSerialApi.Instance;
    }
}
