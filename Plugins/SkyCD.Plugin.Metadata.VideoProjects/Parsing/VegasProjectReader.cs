using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class VegasProjectReader
{
    private static readonly Guid RiffGuid = new("66666972-912e-11cf-a5d6-28db04c10000");
    private static readonly Guid VegasProjectGuid = new("46c429ef-904a-11d2-8722-00c04f8edb8a");
    private static readonly Guid ProjectSettingsGuid = new("b28f2d5a-230f-11d2-86af-00c04f8edb8a");

    public static ProjectFacts? Read(Stream source)
    {
        // The Sonic Foundry container uses GUIDs and inclusive little-endian 64-bit chunk lengths.
        Span<byte> header = stackalloc byte[72];
        if (source.ReadAtLeast(header, header.Length, false) != header.Length ||
            new Guid(header[..16]) != RiffGuid || new Guid(header[24..40]) != VegasProjectGuid ||
            new Guid(header[40..56]) != ProjectSettingsGuid)
            return null;

        var formSize = BinaryPrimitives.ReadUInt64LittleEndian(header[16..]);
        var settingsSize = BinaryPrimitives.ReadUInt64LittleEndian(header[56..]);
        if (formSize != (ulong)source.Length || settingsSize < 32 || settingsSize > formSize - 40)
            return null;

        return new ProjectFacts("Vegas", "veg", "VEGAS Pro")
        {
            ApplicationVersion = BinaryPrimitives.ReadUInt16LittleEndian(header[70..])
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
