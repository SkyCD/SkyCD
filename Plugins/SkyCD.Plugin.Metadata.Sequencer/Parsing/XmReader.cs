using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class XmReader
{
    internal static bool Matches(ReadOnlySpan<byte> data) =>
        data.Length >= 80 && data.StartsWith("Extended Module: "u8) && data[37] == 0x1a;

    internal static SequencerFacts? Read(ReadOnlySpan<byte> data, CancellationToken token)
    {
        var endHeader = 60L + BinaryPrimitives.ReadUInt32LittleEndian(data[60..]);
        var orders = ModuleHeader.Word(data, 64);
        var channels = ModuleHeader.Word(data, 68);
        var patterns = ModuleHeader.Word(data, 70);
        var instruments = ModuleHeader.Word(data, 72);
        if (endHeader < 80 + orders || endHeader > data.Length || orders > 256 || channels is 0 or > 64 ||
            patterns > 256 || instruments > 128)
            return null;

        var facts = ModuleHeader.Header("xm", data.Slice(17, 20), ModuleHeader.Decode(data.Slice(38, 20)), channels, orders,
            patterns, instruments, null, ModuleHeader.Word(data, 78), ModuleHeader.Word(data, 76));
        // XM 1.04 stores all pattern blocks before the instrument blocks.
        if (ModuleHeader.Word(data, 58) != 0x0104)
            return facts;
        var position = (int)endHeader;
        for (var i = 0; i < patterns; i++)
        {
            token.ThrowIfCancellationRequested();
            if (data.Length - position < 9)
                return null;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            var next = position + (long)size + ModuleHeader.Word(data, position + 7);
            if (size < 9 || next > data.Length)
                return null;
            position = (int)next;
        }
        var samples = 0;
        for (var i = 0; i < instruments; i++)
        {
            token.ThrowIfCancellationRequested();
            if (data.Length - position < 29)
                return null;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            var sampleStart = position + (long)size;
            if (size < 29 || sampleStart > data.Length)
                return null;
            var count = ModuleHeader.Word(data, position + 27);
            if (i < 32)
                facts.AddNames([ModuleHeader.Decode(data.Slice(position + 4, 22))], 1);
            if (count != 0)
            {
                if (count > 256 || size < 33)
                    return null;
                var sampleHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 29)..]);
                var endHeaders = sampleStart + (long)count * sampleHeaderSize;
                if (sampleHeaderSize < 40 || endHeaders > data.Length)
                    return null;
                long sampleBytes = 0;
                for (var s = 0; s < count; s++)
                    sampleBytes += BinaryPrimitives.ReadUInt32LittleEndian(data[(int)(sampleStart + s * (long)sampleHeaderSize)..]);
                sampleStart = endHeaders + sampleBytes;
            }
            if (sampleStart > data.Length)
                return null;
            position = (int)sampleStart;
            samples += count;
        }
        facts.Integer("SampleCount", samples);
        return facts;
    }
}
