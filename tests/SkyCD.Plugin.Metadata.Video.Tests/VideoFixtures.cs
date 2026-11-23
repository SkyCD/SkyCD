using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCvSharp;
using SkyCD.Plugin.Metadata.Video.Parsing;

namespace SkyCD.Plugin.Metadata.Video.Tests;

/// <summary>
/// Builds container-accurate video fixtures in memory so the suite commits no binary blobs and every expected
/// value is known to the test that asserts it. Each builder emits the minimum structure a real parser must
/// walk, including the size, length and matrix fields the plugin validates.
/// </summary>
internal static class VideoFixtures
{
    internal static readonly byte[] JpegFrame = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];

    // ---------------------------------------------------------------- MP4 / QuickTime

    /// <summary>
    /// An MP4 with a video track and an audio track, a title atom, and an optional rotation matrix on the
    /// video track. Set <paramref name="videoFirst"/> false to place <c>moov</c> at the end, which is what a
    /// non-faststart file looks like and what forces a reader to skip <c>mdat</c> by offset.
    /// </summary>
    internal static byte[] Mp4(
        uint timescale = 1000,
        uint duration = 5000,
        int width = 1920,
        int height = 1080,
        int rotationDegrees = 0,
        string videoCodec = "avc1",
        string audioCodec = "mp4a",
        uint videoTimescale = 24000,
        uint frameDelta = 1000,
        string? title = "Test Video",
        bool withAudioTrack = true,
        bool videoFirst = true,
        int mdatBytes = 4096,
        string brand = "isom")
    {
        var mvhd = Atom("mvhd", Concat(
            BigEndian(0u),                       // version and flags
            BigEndian(0u), BigEndian(0u),        // creation and modification time
            BigEndian(timescale), BigEndian(duration),
            new byte[80]));                      // rate, volume, matrix and the rest

        var videoTrak = Trak(videoCodec, "vide", width, height, rotationDegrees, videoTimescale, frameDelta, video: true);
        var children = new List<byte[]> { mvhd, videoTrak };
        if (withAudioTrack)
        {
            children.Add(Trak(audioCodec, "soun", 0, 0, 0, 44100, 1024, video: false));
        }

        if (title is not null)
        {
            children.Add(Atom("udta", Atom("meta", Concat(BigEndian(0u), ItemList(title)))));
        }

        var moov = Atom("moov", Concat(children.ToArray()));
        var ftyp = Atom("ftyp", Concat(
            Encoding.ASCII.GetBytes(brand), BigEndian(512u), Encoding.ASCII.GetBytes(brand + "mp42")));
        var mdat = Atom("mdat", new byte[mdatBytes]);

        return videoFirst ? Concat(ftyp, moov, mdat) : Concat(ftyp, mdat, moov);
    }

    /// <summary>A QuickTime file, distinguished from MP4 only by its major brand.</summary>
    internal static byte[] QuickTime() => Mp4(videoCodec: "hvc1", title: null, brand: "qt  ");

    private static byte[] Trak(
        string codec,
        string handler,
        int width,
        int height,
        int rotationDegrees,
        uint timescale,
        uint sampleDelta,
        bool video)
    {
        var tkhd = Atom("tkhd", Concat(
            BigEndian(0u),                                     // version and flags
            BigEndian(0u), BigEndian(0u),                      // creation and modification time
            BigEndian(1u),                                     // track id
            BigEndian(0u),                                     // reserved
            BigEndian(0u),                                     // duration
            new byte[8],                                       // reserved
            new byte[8],                                       // layer, alternate group, volume, reserved
            Matrix(rotationDegrees),
            BigEndian((uint)width << 16), BigEndian((uint)height << 16)));

        var mdhd = Atom("mdhd", Concat(
            BigEndian(0u),
            BigEndian(0u), BigEndian(0u),
            BigEndian(timescale), BigEndian(timescale * 5u),
            BigEndian(0x55C40000u)));

        var hdlr = Atom("hdlr", Concat(
            BigEndian(0u),
            BigEndian(0u),
            Encoding.ASCII.GetBytes(handler),
            new byte[12],
            Encoding.ASCII.GetBytes("Handler\0")));

        var entry = video ? VideoSampleEntry(codec, width, height) : AudioSampleEntry(codec);
        var stsd = Atom("stsd", Concat(BigEndian(0u), BigEndian(1u), entry));
        var stts = Atom("stts", Concat(BigEndian(0u), BigEndian(1u), BigEndian(120u), BigEndian(sampleDelta)));
        var stbl = Atom("stbl", Concat(stsd, stts));
        var minf = Atom("minf", stbl);
        var mdia = Atom("mdia", Concat(mdhd, hdlr, minf));

        return Atom("trak", Concat(tkhd, mdia));
    }

    private static byte[] VideoSampleEntry(string codec, int width, int height) => Concat(
        BigEndian(86u),
        Encoding.ASCII.GetBytes(codec),
        new byte[6],
        BigEndian(1u, 2),           // data reference index
        new byte[2],                // pre-defined
        new byte[2],                // reserved
        new byte[12],               // pre-defined
        BigEndian((uint)width, 2),
        BigEndian((uint)height, 2),
        BigEndian(0x00480000u),     // horizontal resolution 72 dpi
        BigEndian(0x00480000u),     // vertical resolution
        new byte[4],                // reserved
        BigEndian(1u, 2),           // frame count
        new byte[32],               // compressor name
        BigEndian(0x0018u, 2),      // depth
        BigEndian(0xFFFFu, 2));     // pre-defined

    private static byte[] AudioSampleEntry(string codec) => Concat(
        BigEndian(36u),
        Encoding.ASCII.GetBytes(codec),
        new byte[6],
        BigEndian(1u, 2),
        new byte[8],
        BigEndian(2u, 2),           // channel count
        BigEndian(16u, 2),          // sample size
        new byte[4],                // pre-defined and reserved
        BigEndian(44100u << 16));   // sample rate as 16.16

    /// <summary>The QuickTime transformation matrix, stored row-major as a, b, u, c, d, v, x, y, w.</summary>
    internal static byte[] Matrix(int rotationDegrees)
    {
        var (a, b, c, d) = rotationDegrees switch
        {
            90 => (0, 1, -1, 0),
            180 => (-1, 0, 0, -1),
            270 => (0, -1, 1, 0),
            _ => (1, 0, 0, 1)
        };

        return Concat(
            Fixed16(a), Fixed16(b), BigEndian(0u),
            Fixed16(c), Fixed16(d), BigEndian(0u),
            BigEndian(0u), BigEndian(0u), BigEndian(0x40000000u));
    }

    private static byte[] ItemList(string title) =>
        Atom("ilst", Atom(new byte[] { 0xA9, (byte)'n', (byte)'a', (byte)'m' },
            Atom("data", Concat(BigEndian(1u), BigEndian(0u), Encoding.UTF8.GetBytes(title)))));

    private static byte[] Fixed16(int value) => BigEndian(unchecked((uint)(value * 65536)));

    // ---------------------------------------------------------------- Matroska / WebM

    /// <summary>
    /// A Matroska or WebM file with one video track and one audio track. The Segment can be written with an
    /// unknown size, which is what streaming writers emit and what forces a reader to honour the parent end.
    /// </summary>
    internal static byte[] Matroska(
        string docType = "matroska",
        uint timecodeScale = 1_000_000,
        double duration = 5000.0,
        string title = "Test Matroska",
        string videoCodec = "V_VP9",
        string audioCodec = "A_OPUS",
        int pixelWidth = 1280,
        int pixelHeight = 720,
        int? displayWidth = null,
        int? displayHeight = null,
        double? projectionRoll = null,
        ulong defaultDurationNanoseconds = 33_333_333,
        int channels = 2,
        bool unknownSegmentSize = false,
        int clusterBytes = 4096)
    {
        var videoTrackBody = Concat(
            Element(0xE0, Concat(
                Element(0xB0, UInt(pixelWidth)),
                Element(0xBA, UInt(pixelHeight)),
                displayWidth is null ? [] : Element(0x54B0, UInt(displayWidth.Value)),
                displayHeight is null ? [] : Element(0x54BA, UInt(displayHeight.Value)),
                projectionRoll is null ? [] : Element(0x7670, Element(0x76BA, Float(projectionRoll.Value))))));

        var videoTrack = Element(0xAE, Concat(
            Element(0x83, UInt(1)),
            Element(0x86, Text(videoCodec)),
            Element(0x23E383, UInt64(defaultDurationNanoseconds)),
            videoTrackBody));

        var audioTrack = Element(0xAE, Concat(
            Element(0x83, UInt(2)),
            Element(0x86, Text(audioCodec)),
            Element(0xE1, Concat(Element(0x9F, UInt(channels)), Element(0xB5, Float(48000.0))))));

        var info = Element(0x1549A966, Concat(
            Element(0x2AD7B1, UInt(timecodeScale)),
            Element(0x4489, Float(duration)),
            Element(0x7BA9, Text(title))));

        var segmentBody = Concat(info, Element(0x1654AE6B, Concat(videoTrack, audioTrack)), new byte[clusterBytes]);
        var segment = unknownSegmentSize
            ? Concat(Id(0x18538067), UnknownSize(8), segmentBody)
            : Element(0x18538067, segmentBody);

        var ebmlHeader = Element(0x1A45DFA3, Concat(
            Element(0x4282, Text(docType)),
            Element(0x4287, UInt(4)),
            Element(0x4285, UInt(2))));

        return Concat(ebmlHeader, segment);
    }

    /// <summary>WebM is Matroska with a different document type and a restricted codec set.</summary>
    internal static byte[] Webm() => Matroska(docType: "webm", videoCodec: "V_VP9", audioCodec: "A_OPUS", title: "Test WebM");

    // ---------------------------------------------------------------- AVI

    /// <summary>An AVI with one video stream and one audio stream inside a standard <c>hdrl</c> list.</summary>
    internal static byte[] Avi(
        uint microSecondsPerFrame = 40000,
        uint totalFrames = 250,
        int width = 640,
        int height = 480,
        string videoHandler = "H264",
        uint streamRate = 25,
        uint streamScale = 1,
        uint streamLength = 250,
        ushort audioFormatTag = 0x0055,
        bool withAudioStream = true,
        int moviBytes = 4096)
    {
        var avih = Chunk("avih", Concat(
            LittleEndian(microSecondsPerFrame),
            LittleEndian(0u),                       // max bytes per second
            LittleEndian(0u),                       // padding granularity
            LittleEndian(0x10u),                    // flags: AVIF_HASINDEX
            LittleEndian(totalFrames),
            LittleEndian(0u),                       // initial frames
            LittleEndian(withAudioStream ? 2u : 1u),// streams
            LittleEndian(0u),                       // suggested buffer size
            LittleEndian((uint)width),
            LittleEndian((uint)height),
            new byte[16]));

        var videoStream = List("strl", Concat(
            Chunk("strh", StreamHeader("vids", videoHandler, streamScale, streamRate, streamLength)),
            Chunk("strf", BitmapInfoHeader(width, height, videoHandler))));

        var streams = new List<byte[]> { avih, videoStream };
        if (withAudioStream)
        {
            streams.Add(List("strl", Concat(
                Chunk("strh", StreamHeader("auds", "    ", 1, 44100, 0)),
                Chunk("strf", WaveFormatEx(audioFormatTag, channels: 2, sampleRate: 44100)))));
        }

        var hdrl = List("hdrl", Concat(streams.ToArray()));
        var movi = List("movi", new byte[moviBytes]);

        var body = Concat(hdrl, movi);
        return Concat(Encoding.ASCII.GetBytes("RIFF"), LittleEndian((uint)(body.Length + 4)),
            Encoding.ASCII.GetBytes("AVI "), body);
    }

    private static byte[] StreamHeader(string type, string handler, uint scale, uint rate, uint length) => Concat(
        Encoding.ASCII.GetBytes(type),
        Encoding.ASCII.GetBytes(handler.PadRight(4).Substring(0, 4)),
        LittleEndian(0u),                       // flags
        LittleEndian(0u),                       // priority and language
        LittleEndian(0u),                       // initial frames
        LittleEndian(scale),
        LittleEndian(rate),
        LittleEndian(0u),                       // start
        LittleEndian(length),
        LittleEndian(0u),                       // suggested buffer size
        LittleEndian(0xFFFFFFFFu),              // quality
        LittleEndian(0u),                       // sample size
        new byte[8]);                           // frame rectangle

    private static byte[] BitmapInfoHeader(int width, int height, string compression) => Concat(
        LittleEndian(40u),
        LittleEndian((uint)width),
        LittleEndian((uint)height),
        LittleEndian(1u, 2),                    // planes
        LittleEndian(24u, 2),                   // bit count
        Encoding.ASCII.GetBytes(compression.PadRight(4).Substring(0, 4)),
        new byte[20]);

    private static byte[] WaveFormatEx(ushort formatTag, ushort channels, uint sampleRate) => Concat(
        LittleEndian((uint)formatTag, 2),
        LittleEndian(channels, 2),
        LittleEndian(sampleRate),
        LittleEndian(sampleRate * channels * 2),
        LittleEndian((uint)(channels * 2), 2),
        LittleEndian(16u, 2),
        LittleEndian(0u, 2));

    /// <summary>
    /// A genuinely decodable MJPEG-in-AVI clip. The JPEG frames are encoded at runtime, so the suite still
    /// commits no binary and the bundled decoder is exercised against a real bitstream rather than headers.
    /// </summary>
    internal static byte[] MjpegAvi(int width = 64, int height = 48, int fps = 10, int frames = 5)
    {
        if (!OpenCvNativeBootstrap.EnsureLoaded())
        {
            throw new InvalidOperationException("The OpenCV native runtime is not available on this platform.");
        }

        var encodedFrames = new byte[frames][];
        for (var index = 0; index < frames; index++)
        {
            using var image = new Mat(
                new Size(width, height),
                MatType.CV_8UC3,
                new Scalar(20 + (index * 30), 60, 200 - (index * 25)));
            Cv2.ImEncode(".jpg", image, out var jpeg);
            encodedFrames[index] = jpeg;
        }

        var avih = Chunk("avih", Concat(
            LittleEndian((uint)(1_000_000 / fps)),
            LittleEndian(0u),
            LittleEndian(0u),
            LittleEndian(0x10u),                    // flags: AVIF_HASINDEX
            LittleEndian((uint)frames),
            LittleEndian(0u),
            LittleEndian(1u),                       // streams
            LittleEndian(0u),
            LittleEndian((uint)width),
            LittleEndian((uint)height),
            new byte[16]));

        var strh = Chunk("strh", Concat(
            Encoding.ASCII.GetBytes("vids"),
            Encoding.ASCII.GetBytes("MJPG"),
            LittleEndian(0u),
            LittleEndian(0u, 2),
            LittleEndian(0u, 2),
            LittleEndian(0u),
            LittleEndian(1u),                       // scale
            LittleEndian((uint)fps),                // rate
            LittleEndian(0u),
            LittleEndian((uint)frames),
            LittleEndian(0u),
            LittleEndian(0xFFFFFFFFu),              // quality
            LittleEndian(0u),
            new byte[8]));

        var strf = Chunk("strf", Concat(
            LittleEndian(40u),
            LittleEndian((uint)width),
            LittleEndian((uint)height),
            LittleEndian(1u, 2),                    // planes
            LittleEndian(24u, 2),                   // bit count
            Encoding.ASCII.GetBytes("MJPG"),
            new byte[20]));

        var hdrl = List("hdrl", Concat(avih, List("strl", Concat(strh, strf))));
        var movi = List("movi", Concat(encodedFrames.Select(frame => Chunk("00dc", frame)).ToArray()));
        var body = Concat(Encoding.ASCII.GetBytes("AVI "), hdrl, movi);

        return Concat(Encoding.ASCII.GetBytes("RIFF"), LittleEndian((uint)body.Length), body);
    }

    private static byte[] Chunk(string fourCc, byte[] payload)
    {
        // RIFF chunks are word aligned; an odd payload carries one padding byte outside the declared size.
        var padding = payload.Length % 2 == 1 ? new byte[] { 0 } : [];
        return Concat(Encoding.ASCII.GetBytes(fourCc), LittleEndian((uint)payload.Length), payload, padding);
    }

    private static byte[] List(string fourCc, byte[] payload) =>
        Chunk("LIST", Concat(Encoding.ASCII.GetBytes(fourCc), payload));

    // ---------------------------------------------------------------- EBML primitives

    private static byte[] Element(uint id, byte[] payload) =>
        Concat(Id(id), Size(payload.Length), payload);

    private static byte[] Id(uint id)
    {
        var width = id <= 0xFF ? 1 : id <= 0xFFFF ? 2 : id <= 0xFFFFFF ? 3 : 4;
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
        {
            bytes[width - 1 - i] = (byte)(id >> (i * 8));
        }

        return bytes;
    }

    private static byte[] Size(long value)
    {
        for (var width = 1; width <= 8; width++)
        {
            var capacity = 1L << (7 * width);
            if (value >= capacity - 1)
            {
                continue;
            }

            var bytes = new byte[width];
            var marker = (byte)(1 << (8 - width));
            var remaining = value;
            for (var i = width - 1; i >= 0; i--)
            {
                bytes[i] = (byte)(remaining & 0xFF);
                remaining >>= 8;
            }

            bytes[0] |= marker;
            return bytes;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "The element size does not fit in eight bytes.");
    }

    /// <summary>An all-ones size, meaning the element runs to the end of its parent.</summary>
    private static byte[] UnknownSize(int width)
    {
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
        {
            bytes[i] = 0xFF;
        }

        bytes[0] = (byte)(1 << (8 - width));
        return bytes;
    }

    private static byte[] UInt(long value)
    {
        var width = value <= 0xFF ? 1 : value <= 0xFFFF ? 2 : value <= 0xFFFFFF ? 3 :
            value <= 0xFFFFFFFF ? 4 : value <= 0xFFFFFFFFFF ? 5 : value <= 0xFFFFFFFFFFFF ? 6 :
            value <= 0xFFFFFFFFFFFFFF ? 7 : 8;
        return UInt64((ulong)value, width);
    }

    private static byte[] UInt64(ulong value, int width = 8)
    {
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
        {
            bytes[width - 1 - i] = (byte)(value >> (i * 8));
        }

        return bytes;
    }

    private static byte[] Float(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var bytes = new byte[8];
        for (var i = 0; i < 8; i++)
        {
            bytes[i] = (byte)(bits >> ((7 - i) * 8));
        }

        return bytes;
    }

    private static byte[] Text(string value) => Encoding.UTF8.GetBytes(value);

    // ---------------------------------------------------------------- shared helpers

    internal static byte[] NotVideo(int length = 64)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    internal static byte[] HeaderOnly(byte[] data, int length = 16) => data[..Math.Min(length, data.Length)];

    private static byte[] Atom(string type, byte[] payload) =>
        Atom(Encoding.ASCII.GetBytes(type), payload);

    private static byte[] Atom(byte[] type, byte[] payload) =>
        Concat(BigEndian((uint)(payload.Length + 8)), type, payload);

    private static byte[] BigEndian(uint value, int width = 4)
    {
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
        {
            bytes[width - 1 - i] = (byte)(value >> (i * 8));
        }

        return bytes;
    }

    private static byte[] LittleEndian(uint value, int width = 4)
    {
        var bytes = new byte[width];
        for (var i = 0; i < width; i++)
        {
            bytes[i] = (byte)(value >> (i * 8));
        }

        return bytes;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        using var stream = new MemoryStream();
        foreach (var part in parts.Where(part => part is { Length: > 0 }))
        {
            stream.Write(part, 0, part.Length);
        }

        return stream.ToArray();
    }
}
