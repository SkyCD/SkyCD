using System;
using System.Buffers.Binary;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Decodes the composition record of an After Effects <c>cdta</c> chunk. Fields are read only when they fit
/// and fall inside a plausible range; anything else stays absent instead of becoming a made-up value.
/// </summary>
internal static class CompositionFacts
{
    private const double DefaultTimeBase = 30_720;

    public static void Read(byte[] data, out string? resolution, out double? frameRate, out double? durationSeconds)
    {
        resolution = null;
        frameRate = null;
        durationSeconds = null;

        var width = ReadUInt16(data, 140);
        var height = ReadUInt16(data, 142);
        if (width is > 0 and <= 32768 && height is > 0 and <= 32768)
        {
            resolution = $"{width.Value}x{height.Value}";
        }

        var integerRate = ReadUInt16(data, 156);
        var fraction = ReadUInt16(data, 158) ?? 0;
        if (integerRate is >= 1 and <= 240)
        {
            frameRate = Math.Round(integerRate.Value + (fraction / 65536.0), 3);
        }
        else
        {
            var rawRate = ReadUInt32(data, 168);
            if (rawRate is >= 1 and <= 240)
            {
                frameRate = rawRate.Value;
            }
        }

        var timeBase = ReadUInt16(data, 10);
        var ticksPerSecond = timeBase is >= 100 ? timeBase.Value : DefaultTimeBase;

        var durationTicks = ReadUInt32(data, 44);
        if (durationTicks is > 0 and < 100_000_000)
        {
            durationSeconds = Math.Round(durationTicks.Value / ticksPerSecond, 3);
            return;
        }

        var durationFrames = ReadUInt32(data, 16);
        if (frameRate is > 0 && durationFrames is > 0 and < 1_000_000)
        {
            durationSeconds = Math.Round(durationFrames.Value / frameRate.Value, 3);
        }
    }

    private static ushort? ReadUInt16(byte[] data, int offset) =>
        data.Length >= offset + 2 ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset, 2)) : null;

    private static uint? ReadUInt32(byte[] data, int offset) =>
        data.Length >= offset + 4 ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4)) : null;
}
