using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.Models3D.Exceptions;
using SkyCD.Plugin.Metadata.Models3D.Models;

namespace SkyCD.Plugin.Metadata.Models3D.Parsing;

internal static class GlbReader
{
    public static void Read(byte[] data, long sourceLength, ModelScan scan)
    {
        if (data.Length < 20 && scan.Truncated)
            return;
        if (data.Length < 20 || !data.AsSpan(0, 4).SequenceEqual("glTF"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)) != sourceLength ||
            !data.AsSpan(16, 4).SequenceEqual("JSON"u8))
            throw new ModelReadException("InvalidHeader");

        var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        if (size > sourceLength - 20 || size % 4 != 0)
            throw new ModelReadException("InvalidChunk");
        if (size > data.Length - 20)
            return;

        GltfReader.Read(data.AsMemory(20, (int)size), data, scan, binary: true);
    }
}
