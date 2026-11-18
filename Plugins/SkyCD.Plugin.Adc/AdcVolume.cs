using System.Collections.Generic;

namespace SkyCD.Plugin.Adc;

/// <summary>
/// A cataloged volume with its label, media type and preserved optional properties.
/// </summary>
public sealed class AdcVolume
{
    public uint VolumeId { get; set; }

    public AdcVolumeType VolumeType { get; set; }

    public byte Flags { get; set; }

    public uint SerialNumber { get; set; }

    public ulong CapacityBytes { get; set; }

    public string Label { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public List<AdcProperty> Properties { get; } = [];
}
