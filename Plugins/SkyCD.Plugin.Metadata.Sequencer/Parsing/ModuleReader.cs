using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using SkyCD.Plugin.Metadata.Sequencer.Models;

namespace SkyCD.Plugin.Metadata.Sequencer.Parsing;

internal static class ModuleReader
{
    internal static SequencerFacts? Read(byte[] bytes, string extension, CancellationToken token)
    {
        var data = bytes.AsSpan();
        if (extension == ".xrns" && data.StartsWith("PK\x03\x04"u8))
            return ReadRenoise(bytes, token);
        if (data.Length >= 80 && data.StartsWith("Extended Module: "u8) && data[37] == 0x1a)
            return ReadXm(data, token);
        if (data.Length >= 96 && data[28] == 0x1a && data[29] == 16 && data[44..].StartsWith("SCRM"u8))
            return ReadS3m(data);
        if (data.Length >= 192 && (data.StartsWith("IMPM"u8) || data.StartsWith("tpm."u8)))
            return ReadIt(data, extension);
        if (data.Length > 8 && data.StartsWith("MO3"u8) && data[3] <= 5 &&
            BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) is > 0 and < 0x20000000)
        {
            if (data[3] == 5 && (data.Length < 13 ||
                BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) > data.Length - 12))
                return null;
            var mo3 = new SequencerFacts("Module", "mo3");
            mo3.Text("Tracker", "MO3");
            mo3.Text("ApplicationVersion", data[3].ToString(System.Globalization.CultureInfo.InvariantCulture));
            return mo3;
        }
        return ReadMod(data);
    }

    private static SequencerFacts? ReadMod(ReadOnlySpan<byte> data)
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

        var facts = Header("mod", data[..20], signature, channels, orders.Length, patterns, null, samples, 125, 6);
        for (var i = 0; i < 31; i++)
            facts.AddNames([Decode(data.Slice(20 + i * 30, 22))], 1);
        return facts;
    }

    private static SequencerFacts? ReadXm(ReadOnlySpan<byte> data, CancellationToken token)
    {
        var endHeader = 60L + BinaryPrimitives.ReadUInt32LittleEndian(data[60..]);
        var orders = Word(data, 64);
        var channels = Word(data, 68);
        var patterns = Word(data, 70);
        var instruments = Word(data, 72);
        if (endHeader < 80 + orders || endHeader > data.Length || orders > 256 || channels is 0 or > 64 ||
            patterns > 256 || instruments > 128)
            return null;

        var facts = Header("xm", data.Slice(17, 20), Decode(data.Slice(38, 20)), channels, orders,
            patterns, instruments, null, Word(data, 78), Word(data, 76));
        // XM 1.04 stores all pattern blocks before the instrument blocks.
        if (Word(data, 58) != 0x0104)
            return facts;
        var position = (int)endHeader;
        for (var i = 0; i < patterns; i++)
        {
            token.ThrowIfCancellationRequested();
            if (data.Length - position < 9)
                return null;
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data[position..]);
            var next = position + (long)size + Word(data, position + 7);
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
            var count = Word(data, position + 27);
            if (i < 32)
                facts.AddNames([Decode(data.Slice(position + 4, 22))], 1);
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

    private static SequencerFacts? ReadS3m(ReadOnlySpan<byte> data)
    {
        var orders = Word(data, 32);
        var instruments = Word(data, 34);
        var patterns = Word(data, 36);
        if (96 + orders + 2 * (instruments + patterns) > data.Length)
            return null;

        var channels = data.Slice(64, 32).ToArray().Count(setting => setting < 32);
        var samples = 0;
        var names = new List<string>();
        for (var i = 0; i < instruments; i++)
        {
            var pointer = Word(data, 96 + orders + i * 2) * 16;
            if (pointer == 0)
                continue;
            if (pointer > data.Length - 80)
                return null;
            if (data[pointer] == 1)
                samples++;
            if (names.Count < 32)
                names.Add(Decode(data.Slice(pointer + 48, 28)));
        }

        var facts = Header("s3m", data[..28], $"Scream Tracker (0x{Word(data, 40):X4})", channels,
            orders, patterns, instruments, samples, data[50], data[49]);
        facts.AddNames(names, 32);
        return facts;
    }

    private static SequencerFacts? ReadIt(ReadOnlySpan<byte> data, string extension)
    {
        var orders = Word(data, 32);
        var instruments = Word(data, 34);
        var samples = Word(data, 36);
        var patterns = Word(data, 38);
        if (192 + orders + 4 * (instruments + samples + patterns) > data.Length)
            return null;

        var format = data.StartsWith("tpm."u8) || (extension == ".mptm" && Word(data, 40) >= 0x0888) ? "mptm" : "it";
        var channels = data.Slice(64, 64).ToArray().Count(pan => pan < 128);
        var facts = Header(format, data.Slice(4, 26), format == "mptm" ? "OpenMPT" : $"Impulse Tracker (0x{Word(data, 40):X4})",
            channels, orders, patterns, instruments, samples, data[51], data[50]);
        for (var i = 0; i < instruments + samples; i++)
        {
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data[(192 + orders + i * 4)..]);
            if (offset == 0)
                continue;
            if (offset > data.Length - 58)
                return null;
            var header = data[(int)offset..];
            if (!header.StartsWith(i < instruments ? "IMPI"u8 : "IMPS"u8))
                return null;
            if (facts.Names.Count < 32)
                facts.AddNames([Decode(header.Slice(32 - (i < instruments ? 0 : 12), 26))], 1);
        }
        return facts;
    }

    private static SequencerFacts? ReadRenoise(byte[] bytes, CancellationToken token)
    {
        using var input = new MemoryStream(bytes);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry("Song.xml");
        const int limit = 32 * 1024 * 1024;
        if (entry is null || entry.Length > limit)
            return null;
        using var stream = entry.Open();
        var root = ProjectReader.ReadXml(stream, limit, token);
        if (root.Name != "RenoiseSong")
            return null;

        var facts = new SequencerFacts("Module", "xrns");
        var global = root.Element("GlobalSongData");
        var tracks = root.Element("Tracks")?.Elements();
        facts.Text("Application", "Renoise");
        facts.Text("ApplicationVersion", (string?)root.Attribute("doc_version"));
        facts.Text("Title", (string?)global?.Element("SongName"));
        facts.Text("Artist", (string?)global?.Element("Artist"));
        facts.Text("Genre", (string?)global?.Element("Genre"));
        ProjectReader.SetTempo(facts, (string?)global?.Element("BeatsPerMin"));
        facts.Integer("TrackCount", tracks?.Count() ?? 0);
        facts.Integer("InstrumentCount", Count(root.Element("Instruments"), "Instrument"));
        facts.Integer("PatternCount", Count(root.Element("PatternPool")?.Element("Patterns"), "Pattern"));
        facts.AddNames(tracks?.Select(track => (string?)track.Element("Name")) ?? [], 24);
        return facts;
    }

    private static int Count(XElement? parent, string element) => parent?.Elements(element).Count() ?? 0;
    private static ushort Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    private static string Decode(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data).Split('\0', 2)[0];

    private static SequencerFacts Header(string format, ReadOnlySpan<byte> title, string tracker, int channels,
        int orders, int patterns, int? instruments, int? samples, int tempo, int speed)
    {
        var facts = new SequencerFacts("Module", format);
        facts.Text("Title", Decode(title));
        facts.Text("Tracker", tracker);
        facts.Integer("ChannelCount", channels);
        facts.Integer("OrderCount", orders);
        facts.Integer("PatternCount", patterns);
        if (instruments.HasValue)
            facts.Integer("InstrumentCount", instruments.Value);
        if (samples.HasValue)
            facts.Integer("SampleCount", samples.Value);
        if (tempo > 0)
            facts.Number("Tempo", tempo);
        if (speed > 0)
            facts.Integer("Speed", speed);
        return facts;
    }
}
