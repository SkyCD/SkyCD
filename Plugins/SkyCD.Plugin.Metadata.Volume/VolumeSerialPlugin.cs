using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Volume.Models;
using SkyCD.Plugin.Metadata.Volume.Sources;

namespace SkyCD.Plugin.Metadata.Volume;

/// <summary>
/// Captures the filesystem volume serial of a mounted volume or a data disc while the host captures media
/// info. An audio CD exposes no filesystem, so its capture returns nothing. Absence is reported by returning
/// no capture, so a genuinely reported zero serial is never stored as a failure and a failure never fabricates
/// a placeholder. The plugin degrades to a no-op on unsupported platforms.
/// </summary>
public sealed class VolumeSerialPlugin : IMediaTargetMetadataCapability
{
    private readonly IVolumeSerialApi api;

    public VolumeSerialPlugin()
        : this(CreateDefaultApi())
    {
    }

    internal VolumeSerialPlugin(IVolumeSerialApi api)
    {
        this.api = api;
    }

    public MediaTargetDescriptor Descriptor => new();

    public bool Supports(MediaTarget target) => target.Kind is MediaTargetKind.Volume or MediaTargetKind.OpticalDisc;

    public Task<MediaTargetCapture?> CaptureAsync(MediaTarget target, IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();
        if (!api.IsSupported || string.IsNullOrWhiteSpace(target.Location)
            || !api.TryGetVolumeSerial(target.Location, out var volumeSerial))
        {
            return Task.FromResult<MediaTargetCapture?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<MediaTargetCapture?>(new MediaTargetCapture(VolumeSerialSchema.Metadata,
            [
                new MediaMetadataValue(VolumeSerialSchema.VolumeSerialNumber, MediaMetadataType.Text,
                    Text: VolumeSerialSchema.Format(volumeSerial))
            ]));
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
