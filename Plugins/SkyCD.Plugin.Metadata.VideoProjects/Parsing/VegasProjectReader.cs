using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// Reads the header of the Sonic Foundry RIFF container VEGAS Pro writes. The layout comes from a real VEGAS Pro 8
/// project (#1285): lowercase <c>riff</c> magic, little-endian 64-bit form and settings sizes, the VEGAS project and
/// project-settings GUIDs, and the stored application version 64 bytes into the file. Only the version is mapped;
/// timeline and event layout is not confirmed for VEGAS yet.
/// </summary>
internal static class VegasProjectReader
{
    private const int HeaderLength = 72;
    private const int SettingsVersionOffset = 70;

    public static readonly Guid RiffGuid = new("66666972-912e-11cf-a5d6-28db04c10000");
    public static readonly Guid VegasProjectGuid = new("46c429ef-904a-11d2-8722-00c04f8edb8a");
    public static readonly Guid ProjectSettingsGuid = new("b28f2d5a-230f-11d2-86af-00c04f8edb8a");

    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) =>
        prefix.Length >= 40 &&
        new Guid(prefix[..16]) == RiffGuid &&
        new Guid(prefix[24..40]) == VegasProjectGuid;

    public static VideoProjectFacts? Read(Stream source)
    {
        if (source.Length < HeaderLength)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[HeaderLength];
        source.Position = 0;
        source.ReadExactly(header);

        if (new Guid(header[..16]) != RiffGuid ||
            new Guid(header[24..40]) != VegasProjectGuid ||
            new Guid(header[40..56]) != ProjectSettingsGuid)
        {
            return null;
        }

        var formSize = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        var settingsSize = BinaryPrimitives.ReadUInt64LittleEndian(header[56..]);
        if (formSize != (ulong)source.Length || settingsSize < 32 || settingsSize > formSize - 40)
        {
            return null;
        }

        return new VideoProjectFacts(
            VideoProjectIdentity.KindVegas,
            VideoProjectIdentity.FormatVeg,
            VideoProjectIdentity.ApplicationVegas)
        {
            ApplicationVersion = BinaryPrimitives
                .ReadUInt16LittleEndian(header[SettingsVersionOffset..])
                .ToString(CultureInfo.InvariantCulture)
        };
    }
}
