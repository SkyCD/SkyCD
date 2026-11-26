using System.Buffers.Binary;
using System.Text;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ModReader
{
    internal static SequencerFacts? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1084 || data[950] is 0 or > 128)
            return null;

        var signature = Encoding.ASCII.GetString(data.Slice(1080, 4));
        var channels = signature switch
        {
            "M.K." or "M!K!" or "M&K!" or "N.T." or "FLT4" or "4CHN" => 4,
            "FLT8" or "CD81" or "OCTA" or "OKTA" => 8,
            [>= '1' and <= '9', 'C', 'H', 'N'] => signature[0] - '0',
            [>= '1' and <= '9', >= '0' and <= '9', 'C', 'H'] => (signature[0] - '0') * 10 + signature[1] - '0',
            _ => 0
        };
        if (channels is 0 or > 32)
            return null;
        var orders = data.Slice(952, data[950]).ToArray();
        var patterns = orders.Max() + 1;
        var samples = 0;
        var sampleBytes = 0;
        for (var i = 0; i < 31; i++)
        {
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(42 + i * 30)..]) * 2;
            if (length > 0)
            {
                samples++;
                sampleBytes += length;
            }
        }
        if (1084L + patterns * 64 * channels * 4 + sampleBytes > data.Length)
            return null;

        var facts = ModuleHeader.Header("mod", data[..20], signature, channels, orders.Length, patterns, null, samples, 125, 6);
        for (var i = 0; i < 31; i++)
            facts.AddNames([ModuleHeader.Decode(data.Slice(20 + i * 30, 22))], 1);
        return facts;
    }
}
