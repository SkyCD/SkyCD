using System;
using System.Globalization;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Volume.VolumeSerial.Models;

/// <summary>
/// Stable schema identities and field keys published by the volume serial plugin. The value is the
/// filesystem volume serial number exactly as the OS reports it, displayed in the same lossless
/// upper-case hexadecimal form as the Windows <c>vol</c> command (<c>XXXX-XXXX</c>). It identifies a
/// volume and may change when the volume is reformatted; it is not a hardware manufacturer's serial
/// number and not a cryptographically unique identity.
/// </summary>
public static class VolumeSerialSchema
{
    public const string MetadataSchemaId = "skycd.volume-serial";
    public const int MetadataSchemaVersion = 1;

    /// <summary>Filesystem volume serial number: upper-case <c>XXXX-XXXX</c> hexadecimal.</summary>
    public const string VolumeSerialNumber = "VolumeSerialNumber";

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(VolumeSerialNumber, MediaMetadataType.Text)
    ]);

    /// <summary>Formats the 32-bit serial in the documented, lossless <c>XXXX-XXXX</c> display form.</summary>
    public static string Format(uint volumeSerial) =>
        FormattableString.Invariant($"{(volumeSerial >> 16) & 0xFFFF:X4}-{volumeSerial & 0xFFFF:X4}");

    /// <summary>Parses a formatted serial back to its 32-bit value, proving <see cref="Format"/> is lossless.</summary>
    public static bool TryParse(string? text, out uint volumeSerial)
    {
        volumeSerial = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var hex = text.Replace("-", string.Empty, StringComparison.Ordinal);
        return hex.Length == 8
            && uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out volumeSerial);
    }
}
