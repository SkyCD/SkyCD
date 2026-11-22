using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SkyCD.Plugin.Metadata.Audio.Tests;

/// <summary>
/// Builds container-accurate audio fixtures in memory so the suite commits no binary blobs and every
/// fixture's expected values are known to the test that asserts on them. Each builder emits the minimum
/// structure a real parser must walk, including the size and length fields the plugin validates.
/// </summary>
internal static class AudioFixtures
{
    internal const int MpegFrameSize = 417;
    internal const int MpegSamplesPerFrame = 1152;
    internal const int MpegSampleRate = 44100;
    internal const int MpegBitrateKbps = 128;

    /// <summary>Opaque JPEG bytes; the plugin passes cover art through without decoding it.</summary>
    internal static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

    internal static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    internal static double MpegDuration(int frameCount) => (double)frameCount * MpegSamplesPerFrame / MpegSampleRate;

    // ---------------------------------------------------------------- MP3

    /// <summary>
    /// An MP3 with an ID3v2.3 tag, an APIC front cover and <paramref name="frameCount"/> MPEG1 Layer III
    /// frames. When <paramref name="withXing"/> is set the first frame carries a Xing header, which is what
    /// makes variable-bitrate duration exact rather than estimated from the byte count.
    /// </summary>
    internal static byte[] Mp3(
        int frameCount = 4,
        bool withXing = true,
        bool withId3v1 = true,
        bool withCover = true,
        string title = "Test Title",
        string artist = "Test Artist",
        string album = "Test Album",
        string genre = "Rock",
        string track = "3/12",
        string disc = "1/2",
        string year = "2021",
        byte textEncoding = 0x03)
    {
        using var tag = new MemoryStream();
        WriteTextFrame(tag, "TIT2", title, textEncoding);
        WriteTextFrame(tag, "TPE1", artist, textEncoding);
        WriteTextFrame(tag, "TALB", album, textEncoding);
        WriteTextFrame(tag, "TCON", genre, textEncoding);
        WriteTextFrame(tag, "TRCK", track, textEncoding);
        WriteTextFrame(tag, "TPOS", disc, textEncoding);
        WriteTextFrame(tag, "TYER", year, textEncoding);
        if (withCover)
        {
            WriteApicFrame(tag, "image/jpeg", pictureType: 3, JpegBytes, textEncoding);
        }

        var tagBody = tag.ToArray();
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("ID3"));
        output.WriteByte(3);   // major version 2.3
        output.WriteByte(0);   // revision
        output.WriteByte(0);   // no flags, so no extended header
        WriteSynchsafe(output, tagBody.Length);
        output.Write(tagBody);

        for (var frame = 0; frame < frameCount; frame++)
        {
            WriteMpegFrame(output, frame, frameCount, withXing);
        }

        if (withId3v1)
        {
            WriteId3v1(output, title, artist, album, year, genreIndex: 17, trackNumber: 3);
        }

        return output.ToArray();
    }

    /// <summary>An MP3 carrying no ID3v2 header and no ID3v1 tail, leaving an explicit empty-tag state.</summary>
    internal static byte[] Mp3WithoutTags(int frameCount = 4)
    {
        using var output = new MemoryStream();
        for (var frame = 0; frame < frameCount; frame++)
        {
            WriteMpegFrame(output, frame, frameCount, withXing: true);
        }

        return output.ToArray();
    }

    /// <summary>An MP3 whose only tags are in the ID3v1 tail.</summary>
    internal static byte[] Mp3WithId3v1Only()
    {
        using var output = new MemoryStream();
        for (var frame = 0; frame < 4; frame++)
        {
            WriteMpegFrame(output, frame, 4, withXing: false);
        }

        WriteId3v1(output, "Tail Title", "Tail Artist", "Tail Album", "1999", genreIndex: 17, trackNumber: 7);
        return output.ToArray();
    }

    /// <summary>An MP3 whose ID3v2 tag repeats the artist frame, which is how several performers are stored.</summary>
    internal static byte[] Mp3WithTwoArtists()
    {
        using var tag = new MemoryStream();
        WriteTextFrame(tag, "TPE1", "First Artist", 0x03);
        WriteTextFrame(tag, "TPE1", "Second Artist", 0x03);
        var tagBody = tag.ToArray();

        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("ID3"));
        output.WriteByte(3);
        output.WriteByte(0);
        output.WriteByte(0);
        WriteSynchsafe(output, tagBody.Length);
        output.Write(tagBody);

        for (var frame = 0; frame < 4; frame++)
        {
            WriteMpegFrame(output, frame, 4, withXing: true);
        }

        return output.ToArray();
    }

    private static void WriteMpegFrame(Stream output, int index, int frameCount, bool withXing)
    {
        var frame = new byte[MpegFrameSize];

        // MPEG1 Layer III, no CRC, 128 kbps, 44.1 kHz, no padding, stereo.
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x00;

        if (withXing && index == 0)
        {
            // Side information for MPEG1 stereo is 32 bytes, and the Xing header follows it.
            var offset = 4 + 32;
            Encoding.ASCII.GetBytes("Xing").CopyTo(frame, offset);
            WriteBigEndian(frame, offset + 4, 0x00000003u);   // frames and bytes are present
            WriteBigEndian(frame, offset + 8, (uint)frameCount);
            WriteBigEndian(frame, offset + 12, (uint)(frameCount * MpegFrameSize));
        }

        output.Write(frame, 0, frame.Length);
    }

    private static void WriteId3v1(Stream output, string title, string artist, string album, string year, int genreIndex, int trackNumber)
    {
        var block = new byte[128];
        Encoding.ASCII.GetBytes("TAG").CopyTo(block, 0);
        CopyFixed(block, 3, title, 30);
        CopyFixed(block, 33, artist, 30);
        CopyFixed(block, 63, album, 30);
        CopyFixed(block, 93, year, 4);

        // ID3v1.1: byte 125 must be zero for byte 126 to be read as a track number.
        block[125] = 0;
        block[126] = (byte)trackNumber;
        block[127] = (byte)genreIndex;
        output.Write(block, 0, block.Length);
    }

    private static void WriteTextFrame(Stream output, string id, string value, byte encoding)
    {
        var text = EncodeTagText(value, encoding);
        var payload = new byte[1 + text.Length];
        payload[0] = encoding;
        text.CopyTo(payload, 1);
        WriteFrame(output, id, payload);
    }

    private static void WriteApicFrame(Stream output, string mimeType, byte pictureType, byte[] picture, byte encoding)
    {
        using var payload = new MemoryStream();
        payload.WriteByte(encoding);
        payload.Write(Encoding.ASCII.GetBytes(mimeType));
        payload.WriteByte(0);
        payload.WriteByte(pictureType);
        payload.WriteByte(0);   // empty description, single-byte terminator
        payload.Write(picture, 0, picture.Length);
        WriteFrame(output, "APIC", payload.ToArray());
    }

    private static void WriteFrame(Stream output, string id, byte[] payload)
    {
        output.Write(Encoding.ASCII.GetBytes(id));
        WriteBigEndianStream(output, (uint)payload.Length);   // v2.3 frame sizes are plain integers
        output.WriteByte(0);
        output.WriteByte(0);
        output.Write(payload, 0, payload.Length);
    }

    private static byte[] EncodeTagText(string value, byte encoding) => encoding switch
    {
        0 => Encoding.Latin1.GetBytes(value),
        1 => new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(value)).ToArray(),
        2 => Encoding.BigEndianUnicode.GetBytes(value),
        _ => Encoding.UTF8.GetBytes(value)
    };

    private static void WriteSynchsafe(Stream output, int value)
    {
        output.WriteByte((byte)((value >> 21) & 0x7F));
        output.WriteByte((byte)((value >> 14) & 0x7F));
        output.WriteByte((byte)((value >> 7) & 0x7F));
        output.WriteByte((byte)(value & 0x7F));
    }

    // ---------------------------------------------------------------- FLAC

    /// <summary>
    /// A FLAC file with STREAMINFO, a Vorbis comment block and an optional PICTURE block. The audio payload
    /// is filler; only the metadata blocks matter to a tag reader.
    /// </summary>
    internal static byte[] Flac(
        int sampleRate = 44100,
        int channels = 2,
        int bitDepth = 16,
        long totalSamples = 88200,
        bool withComment = true,
        bool withPicture = true,
        string title = "Flac Title",
        string artist = "Flac Artist",
        string album = "Flac Album",
        int audioBytes = 4096)
    {
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("fLaC"));

        WriteFlacBlock(output, blockType: 0, isLast: false, BuildStreamInfo(sampleRate, channels, bitDepth, totalSamples));

        if (withComment)
        {
            WriteFlacBlock(output, 4, isLast: !withPicture, BuildVorbisComment(
                ("TITLE", title), ("ARTIST", artist), ("ALBUM", album),
                ("TRACKNUMBER", "5"), ("TRACKTOTAL", "11"),
                ("DISCNUMBER", "2"), ("DISCTOTAL", "3"),
                ("DATE", "2019-06-07"), ("GENRE", "Ambient")));
        }

        if (withPicture)
        {
            WriteFlacBlock(output, 6, isLast: true, BuildFlacPicture(3, "image/jpeg", JpegBytes));
        }
        else if (!withComment)
        {
            WriteFlacBlock(output, 1, isLast: true, new byte[16]);
        }

        output.Write(new byte[audioBytes]);
        return output.ToArray();
    }

    private static byte[] BuildStreamInfo(int sampleRate, int channels, int bitDepth, long totalSamples)
    {
        var block = new byte[34];
        WriteBigEndian(block, 0, 4096u, width: 2);   // minimum block size
        WriteBigEndian(block, 2, 4096u, width: 2);   // maximum block size
        // Bytes 4..9 are the minimum and maximum frame size, left at zero for "unknown".
        ulong packed = ((ulong)sampleRate << 44) |
                       ((ulong)(channels - 1) << 41) |
                       ((ulong)(bitDepth - 1) << 36) |
                       ((ulong)totalSamples & 0x0F_FFFF_FFFF);
        for (var i = 0; i < 8; i++)
        {
            block[10 + i] = (byte)(packed >> (56 - (i * 8)));
        }

        return block;
    }

    private static byte[] BuildVorbisComment(params (string Key, string Value)[] comments)
    {
        using var stream = new MemoryStream();
        var vendor = Encoding.UTF8.GetBytes("SkyCD fixture writer");
        WriteLittleEndianStream(stream, (uint)vendor.Length);
        stream.Write(vendor, 0, vendor.Length);
        WriteLittleEndianStream(stream, (uint)comments.Length);

        foreach (var (key, value) in comments)
        {
            var entry = Encoding.UTF8.GetBytes($"{key}={value}");
            WriteLittleEndianStream(stream, (uint)entry.Length);
            stream.Write(entry, 0, entry.Length);
        }

        return stream.ToArray();
    }

    private static byte[] BuildFlacPicture(int pictureType, string mimeType, byte[] data)
    {
        using var stream = new MemoryStream();
        WriteBigEndianStream(stream, (uint)pictureType);
        var mime = Encoding.ASCII.GetBytes(mimeType);
        WriteBigEndianStream(stream, (uint)mime.Length);
        stream.Write(mime, 0, mime.Length);
        WriteBigEndianStream(stream, 0);                    // empty description
        WriteBigEndianStream(stream, 300);                  // width
        WriteBigEndianStream(stream, 300);                  // height
        WriteBigEndianStream(stream, 24);                   // colour depth
        WriteBigEndianStream(stream, 0);                    // palette entries
        WriteBigEndianStream(stream, (uint)data.Length);
        stream.Write(data, 0, data.Length);
        return stream.ToArray();
    }

    private static void WriteFlacBlock(Stream output, int blockType, bool isLast, byte[] payload)
    {
        output.WriteByte((byte)((isLast ? 0x80 : 0) | (blockType & 0x7F)));
        output.WriteByte((byte)((payload.Length >> 16) & 0xFF));
        output.WriteByte((byte)((payload.Length >> 8) & 0xFF));
        output.WriteByte((byte)(payload.Length & 0xFF));
        output.Write(payload, 0, payload.Length);
    }

    // ---------------------------------------------------------------- Ogg Vorbis

    /// <summary>
    /// An Ogg Vorbis stream with identification and comment headers and a final page whose granule position
    /// encodes the total sample count. Page checksums are zero because the plugin does not validate them.
    /// </summary>
    internal static byte[] OggVorbis(
        int sampleRate = 44100,
        int channels = 2,
        long totalSamples = 132300,
        bool withComment = true,
        bool withPicture = true,
        string title = "Ogg Title",
        string artist = "Ogg Artist")
    {
        using var output = new MemoryStream();

        WriteOggPage(output, headerType: 0x02, granule: 0, sequence: 0,
            payload: BuildVorbisIdentification(sampleRate, channels));

        if (withComment)
        {
            var comments = new List<(string Key, string Value)> { ("TITLE", title), ("ARTIST", artist), ("ALBUM", "Ogg Album") };
            if (withPicture)
            {
                comments.Add(("METADATA_BLOCK_PICTURE", Convert.ToBase64String(BuildFlacPicture(3, "image/png", PngBytes))));
            }

            WriteOggPage(output, 0x00, 0, 1, payload: BuildVorbisCommentPacket(comments.ToArray()));
        }

        WriteOggPage(output, 0x04, totalSamples, withComment ? 2u : 1u, payload: new byte[64]);
        return output.ToArray();
    }

    private static byte[] BuildVorbisIdentification(int sampleRate, int channels)
    {
        var packet = new byte[30];
        packet[0] = 0x01;
        Encoding.ASCII.GetBytes("vorbis").CopyTo(packet, 1);
        WriteLittleEndian(packet, 7, 0u);                        // version
        packet[11] = (byte)channels;
        WriteLittleEndian(packet, 12, (uint)sampleRate);
        WriteLittleEndian(packet, 16, 0u);                       // bitrate maximum (unset)
        WriteLittleEndian(packet, 20, 128000u);                  // bitrate nominal
        WriteLittleEndian(packet, 24, 0u);                       // bitrate minimum (unset)
        packet[28] = 0xB8;                                       // blocksize 0 = 1024, blocksize 1 = 2048
        packet[29] = 1;                                          // framing bit
        return packet;
    }

    private static byte[] BuildVorbisCommentPacket((string Key, string Value)[] comments)
    {
        var body = BuildVorbisComment(comments);
        var packet = new byte[7 + body.Length];
        packet[0] = 0x03;
        Encoding.ASCII.GetBytes("vorbis").CopyTo(packet, 1);
        body.CopyTo(packet, 7);
        return packet;
    }

    private static void WriteOggPage(Stream output, byte headerType, long granule, uint sequence, byte[] payload)
    {
        // A packet is split into 255-byte segments; a final segment below 255 terminates it, so an exact
        // multiple needs one extra zero-length segment.
        var segments = new List<byte>();
        var remaining = payload.Length;
        while (remaining >= 255)
        {
            segments.Add(255);
            remaining -= 255;
        }

        segments.Add((byte)remaining);

        output.Write(Encoding.ASCII.GetBytes("OggS"));
        output.WriteByte(0);                 // stream structure version
        output.WriteByte(headerType);
        for (var i = 0; i < 8; i++)
        {
            output.WriteByte((byte)((granule >> (i * 8)) & 0xFF));
        }

        WriteLittleEndianStream(output, 0x534B4344u);   // serial number
        WriteLittleEndianStream(output, sequence);
        WriteLittleEndianStream(output, 0u);            // checksum, not validated
        output.WriteByte((byte)segments.Count);
        foreach (var segment in segments)
        {
            output.WriteByte(segment);
        }

        output.Write(payload, 0, payload.Length);
    }

    // ---------------------------------------------------------------- M4A / MP4

    /// <summary>
    /// An M4A file: <c>ftyp</c>, a <c>moov</c> tree carrying duration and one audio sample entry, and a
    /// <c>udta/meta/ilst</c> list with iTunes-style tags and cover art.
    /// </summary>
    internal static byte[] M4a(
        uint timescale = 44100,
        uint duration = 176400,
        int channels = 2,
        int bitDepth = 16,
        uint sampleRate = 44100,
        bool withTags = true,
        bool withCover = true,
        string codec = "mp4a",
        string title = "M4a Title",
        string artist = "M4a Artist")
    {
        var mvhd = Atom("mvhd", Concat(
            Bytes(0, 0, 0, 0),                       // version and flags
            BigEndian(0u), BigEndian(0u),            // creation and modification time
            BigEndian(timescale), BigEndian(duration),
            new byte[88]));                          // rate, volume, matrix and the rest

        var mdhd = Atom("mdhd", Concat(
            Bytes(0, 0, 0, 0),
            BigEndian(0u), BigEndian(0u),
            BigEndian(timescale), BigEndian(duration),
            Bytes(0x55, 0xC4, 0, 0)));

        var hdlr = Atom("hdlr", Concat(
            Bytes(0, 0, 0, 0),
            BigEndian(0u),
            Encoding.ASCII.GetBytes("soun"),
            new byte[12],
            Encoding.ASCII.GetBytes("SoundHandler\0")));

        var audioEntry = Concat(
            BigEndian(36u),
            Encoding.ASCII.GetBytes(codec),
            new byte[6],
            BigEndian(1u, width: 2),               // data reference index
            new byte[8],
            BigEndian((uint)channels, width: 2),
            BigEndian((uint)bitDepth, width: 2),
            Bytes(0, 0, 0, 0),
            BigEndian((uint)sampleRate << 16));

        var stsd = Atom("stsd", Concat(Bytes(0, 0, 0, 0), BigEndian(1u), audioEntry));
        var stbl = Atom("stbl", Concat(stsd, Atom("stts", Bytes(0, 0, 0, 0, 0, 0, 0, 0))));
        var minf = Atom("minf", stbl);
        var mdia = Atom("mdia", Concat(mdhd, hdlr, minf));
        var trak = Atom("trak", mdia);

        var children = new List<byte[]> { mvhd, trak };
        if (withTags)
        {
            children.Add(Atom("udta", Atom("meta", Concat(Bytes(0, 0, 0, 0), BuildItemList(withCover, title, artist)))));
        }

        var moov = Atom("moov", Concat(children.ToArray()));
        var ftyp = Atom("ftyp", Concat(Encoding.ASCII.GetBytes("M4A "), BigEndian(0u), Encoding.ASCII.GetBytes("M4A mp42isom")));
        var mdat = Atom("mdat", new byte[2048]);

        return Concat(ftyp, moov, mdat);
    }

    private static byte[] BuildItemList(bool withCover, string title, string artist)
    {
        var items = new List<byte[]>
        {
            TextItem(new byte[] { 0xA9, (byte)'n', (byte)'a', (byte)'m' }, title),
            TextItem(new byte[] { 0xA9, (byte)'A', (byte)'R', (byte)'T' }, artist),
            TextItem(new byte[] { 0xA9, (byte)'a', (byte)'l', (byte)'b' }, "M4a Album"),
            TextItem(Encoding.ASCII.GetBytes("aART"), "M4a Album Artist"),
            TextItem(new byte[] { 0xA9, (byte)'d', (byte)'a', (byte)'y' }, "2018-04-05"),
            PairItem(Encoding.ASCII.GetBytes("trkn"), number: 6, total: 9),
            PairItem(Encoding.ASCII.GetBytes("disk"), number: 1, total: 1)
        };

        if (withCover)
        {
            items.Add(Atom(Encoding.ASCII.GetBytes("covr"),
                Atom("data", Concat(Bytes(0, 0, 0, 14), new byte[4], PngBytes))));   // 14 = PNG
        }

        return Atom("ilst", Concat(items.ToArray()));
    }

    private static byte[] TextItem(byte[] key, string value) =>
        Atom(key, Atom("data", Concat(
            Bytes(0, 0, 0, 1),      // flags 1 = UTF-8 text
            new byte[4],
            Encoding.UTF8.GetBytes(value))));

    private static byte[] PairItem(byte[] key, int number, int total) =>
        Atom(key, Atom("data", Concat(
            new byte[4],
            new byte[4],
            Bytes(0, 0, (byte)(number >> 8), (byte)number, (byte)(total >> 8), (byte)total, 0, 0))));

    private static byte[] Atom(string type, byte[] payload) =>
        Atom(Encoding.ASCII.GetBytes(type), payload);

    private static byte[] Atom(byte[] type, byte[] payload)
    {
        var atom = new byte[8 + payload.Length];
        WriteBigEndian(atom, 0, (uint)atom.Length);
        type.CopyTo(atom, 4);
        payload.CopyTo(atom, 8);
        return atom;
    }

    // ---------------------------------------------------------------- shared helpers

    /// <summary>
    /// Bytes that are not audio in any container. The pattern is deterministic and deliberately contains no
    /// 0xFF, so it can never be mistaken for an MPEG frame sync.
    /// </summary>
    internal static byte[] NotAudio(int length = 64)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    /// <summary>A structurally valid file cut down to just its first few bytes.</summary>
    internal static byte[] HeaderOnly(byte[] data, int length = 8) => data[..Math.Min(length, data.Length)];

    internal static byte[] Truncated(byte[] data) => data[..(data.Length / 3)];

    private static void CopyFixed(byte[] target, int offset, string value, int length)
    {
        var bytes = Encoding.Latin1.GetBytes(value);
        Array.Copy(bytes, 0, target, offset, Math.Min(bytes.Length, length));
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value, int width = 4)
    {
        for (var i = 0; i < width; i++)
        {
            target[offset + i] = (byte)(value >> ((width - 1 - i) * 8));
        }
    }

    private static void WriteLittleEndian(byte[] target, int offset, uint value)
    {
        for (var i = 0; i < 4; i++)
        {
            target[offset + i] = (byte)(value >> (i * 8));
        }
    }

    private static void WriteBigEndianStream(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteLittleEndianStream(Stream stream, uint value)
    {
        stream.WriteByte((byte)value);
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 24));
    }

    private static byte[] BigEndian(uint value, int width = 4)
    {
        var bytes = new byte[width];
        WriteBigEndian(bytes, 0, value, width);
        return bytes;
    }

    private static byte[] Bytes(params byte[] values) => values;

    private static byte[] Concat(params byte[][] parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts)
        {
            stream.Write(part, 0, part.Length);
        }

        return stream.ToArray();
    }
}
