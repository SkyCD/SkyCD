using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SkyCD.Plugin.Metadata.VideoProjects.Tests;

/// <summary>
/// Synthetic containers for every claimed format. The bytes follow the structural anchors the readers rely on
/// (Premiere ticks, the RIFX chunk tree, AEPX hexadecimal chunk bodies, the Resolve ZIP layout, the Sonic Foundry
/// RIFF header of VEGAS Pro) so the tests exercise real parsing rather than an invented shorthand.
/// </summary>
internal static class VideoProjectFixtures
{
    private const long TicksPerFrame = 10_584_000_000;

    public static readonly string PremiereXml = $$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <PremiereData Version="3">
          <Project ObjectID="1" Version="25.0"/>
          <Project ObjectRef="1"/>
          <Sequence ObjectID="2"><Name>Main Sequence</Name></Sequence>
          <Sequence ObjectID="3"><Name>B-Roll</Name></Sequence>
          <VideoTrackGroup ObjectRef="4"/>
          <VideoTrackGroup ObjectID="4">
            <Track><TrackItems><VideoClipTrackItem ObjectRef="5"/></TrackItems></Track>
            <Track><TrackItems><VideoClipTrackItem ObjectRef="6"/></TrackItems></Track>
            <FrameRate>{{TicksPerFrame}}</FrameRate>
            <FrameRect>0,0,1920,1080</FrameRect>
          </VideoTrackGroup>
          <AudioTrackGroup ObjectID="7">
            <Track/>
            <Track/>
            <Track/>
          </AudioTrackGroup>
          <VideoClipTrackItem ObjectID="5"><Start>0</Start><End>1270080000000</End></VideoClipTrackItem>
          <VideoClipTrackItem ObjectID="6"><Start>0</Start><End>1270080000000</End></VideoClipTrackItem>
          <AudioClipTrackItem ObjectID="13"><Start>0</Start><End>2540160000000</End></AudioClipTrackItem>
          <ClipProjectItem ObjectID="8"/>
          <BinProjectItem ObjectID="9"/>
          <Media ObjectID="10"><FilePath>D:\Media\clip.mov</FilePath></Media>
          <Media ObjectID="11"><FilePath>D:\Media\broll.mov</FilePath></Media>
          <Media ObjectID="12"><FilePath>Black Video</FilePath></Media>
          <Media ObjectRef="10"/>
        </PremiereData>
        """;

    public static byte[] Premiere(bool compressed)
    {
        var xml = Encoding.UTF8.GetBytes(PremiereXml);
        if (!compressed)
        {
            return xml;
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(xml);
        }

        return output.ToArray();
    }

    public static byte[] GzipBomb(int megabytes) =>
        Gzip(new byte[megabytes * 1024 * 1024]);

    public static byte[] Aep() => Aep(FrameFile());

    /// <summary>Declares a RIFX container larger than the bytes that follow.</summary>
    public static byte[] AepWithLyingContainerSize()
    {
        var file = Aep();
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4, 4), (uint)file.Length + 4096);
        return file;
    }

    /// <summary>Appends a chunk whose declared body runs past the end of the file.</summary>
    public static byte[] AepWithOverrunChunk()
    {
        var file = Aep();
        var tail = new byte[12];
        Encoding.ASCII.GetBytes("gunk").CopyTo(tail, 0);
        BinaryPrimitives.WriteUInt32BigEndian(tail.AsSpan(4, 4), 512);
        var combined = new byte[file.Length + tail.Length];
        file.CopyTo(combined, 0);
        tail.CopyTo(combined, file.Length);
        BinaryPrimitives.WriteUInt32BigEndian(combined.AsSpan(4, 4), (uint)(combined.Length - 8));
        return combined;
    }

    public static byte[] Aepx() => Encoding.UTF8.GetBytes($$"""
        <?xml version="1.0" encoding="UTF-8"?>
        <AfterEffectsProject>
          <head bdata="{{Hex(HeadBody())}}"/>
          <Fold>
            <Item>
              <idta bdata="0001"/>
              <string>Footage</string>
              <Sfdr>
                <Item>
                  <idta bdata="0007"/>
                  <string>clip.mov</string>
                </Item>
              </Sfdr>
            </Item>
            <Item>
              <idta bdata="0004"/>
              <string>Main Comp</string>
              <cdta bdata="{{Hex(CompositionBody())}}"/>
            </Item>
          </Fold>
        </AfterEffectsProject>
        """);

    public static byte[] Resolve() => ResolveZip();

    public static byte[] ResolveWithoutProject() => Zip(("Gallery.xml", Encoding.UTF8.GetBytes("<Gallery/>")));

    private static byte[] Aep(byte[] children)
    {
        using var output = new MemoryStream();
        output.Write("RIFX"u8);
        var sizeBytes = new byte[4];
        output.Write(sizeBytes);
        output.Write("Egg!"u8);
        output.Write(Chunk("head", HeadBody()));
        output.Write(children);

        var data = output.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4, 4), (uint)(data.Length - 8));
        return data;
    }

    private static byte[] FrameFile()
    {
        var footage = List("Item",
            Chunk("idta", [0, 7]),
            Chunk("Utf8", Encoding.UTF8.GetBytes("clip.mov")));
        var folder = List("Item",
            Chunk("idta", [0, 1]),
            Chunk("Utf8", Encoding.UTF8.GetBytes("Footage")),
            List("Sfdr", footage));
        var composition = List("Item",
            Chunk("idta", [0, 4]),
            Chunk("Utf8", Encoding.UTF8.GetBytes("Main Comp")),
            Chunk("cdta", CompositionBody()));

        return List("Fold", folder, composition);
    }

    private static byte[] HeadBody()
    {
        var body = new byte[32];
        new byte[] { 0x0F, 0x11, 0x0E, 0x02 }.CopyTo(body, 4);
        return body;
    }

    private static byte[] CompositionBody()
    {
        var body = new byte[204];
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(10, 2), 30_720);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(44, 4), 30_720 * 5);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(140, 2), 1920);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(142, 2), 1080);
        BinaryPrimitives.WriteUInt16BigEndian(body.AsSpan(156, 2), 24);
        return body;
    }

    private static byte[] Chunk(string id, byte[] body)
    {
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes(id));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(size, (uint)body.Length);
        output.Write(size);
        output.Write(body);
        if ((body.Length & 1) == 1)
        {
            output.WriteByte(0);
        }

        return output.ToArray();
    }

    private static byte[] List(string type, params byte[][] children)
    {
        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes(type));
        foreach (var child in children)
        {
            body.Write(child);
        }

        return Chunk("LIST", body.ToArray());
    }

    private static byte[] ResolveZip()
    {
        var frameRate = BitConverter.GetBytes(24.0);
        var sequence = Encoding.UTF8.GetBytes($$"""
            <Sm2SequenceContainer>
              <Sm2Sequence>
                <Name>Timeline A</Name>
                <VideoTrackVec>
                  <Element><Sm2TiTrack><Sm2TiVideoTrack/></Sm2TiTrack></Element>
                  <Element><Sm2TiTrack><Sm2TiVideoTrack/></Sm2TiTrack></Element>
                </VideoTrackVec>
                <AudioTrackVec>
                  <Element><Sm2TiTrack><Sm2TiAudioTrack/></Sm2TiTrack></Element>
                </AudioTrackVec>
                <Sm2TiVideoClip><Start>0</Start><Duration>120</Duration></Sm2TiVideoClip>
                <Sm2TiVideoClip><Start>120</Start><Duration>120</Duration></Sm2TiVideoClip>
                <Sm2TiAudioClip><Start>0</Start><Duration>240</Duration></Sm2TiAudioClip>
                <MediaFrameRate>{{Hex(frameRate)}}</MediaFrameRate>
              </Sm2Sequence>
            </Sm2SequenceContainer>
            """);

        var project = Encoding.UTF8.GetBytes("""
            <?xml version="1.0" encoding="UTF-8"?>
            <!-- Edited with DaVinci Resolve, DbAppVer="19.1.3.0006" -->
            <SM_Project DbAppVer="19.1.3.0006">
              <MediaVec>
                <Element>
                  <Sm2MpTimelineClip><Name>Timeline A</Name></Sm2MpTimelineClip>
                  <Sm2MpTimelineClip><Name>Timeline B</Name></Sm2MpTimelineClip>
                </Element>
              </MediaVec>
            </SM_Project>
            """);

        var mediaPool = Encoding.UTF8.GetBytes("""
            <SM_MediaPool>
              <Sm2MpFolder>
                <Name>Master</Name>
                <Sm2MpVideoClip><Name>clip1.mov</Name></Sm2MpVideoClip>
                <Sm2MpAudioClip><Name>music.wav</Name></Sm2MpAudioClip>
              </Sm2MpFolder>
            </SM_MediaPool>
            """);

        return Zip(
            ("project.xml", project),
            ("MediaPool/Master/MpFolder.xml", mediaPool),
            ("SeqContainer/2f5a6f7c-1d33-4c5e-9c1a-0f0e7a8b9c10.xml", sequence),
            ("Gallery.xml", Encoding.UTF8.GetBytes("<Gallery/>")));
    }

    /// <summary>
    /// The Sonic Foundry RIFF header of a VEGAS Pro project: GUID magic, form size equal to the file length,
    /// the VEGAS and project-settings GUIDs, a 32-byte settings chunk and the stored version at offset 70.
    /// </summary>
    public static byte[] Vegas(ushort version = 8)
    {
        var project = new byte[72];
        new Guid("66666972-912e-11cf-a5d6-28db04c10000").TryWriteBytes(project.AsSpan(0, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(project.AsSpan(16), (ulong)project.Length);
        new Guid("46c429ef-904a-11d2-8722-00c04f8edb8a").TryWriteBytes(project.AsSpan(24, 16));
        new Guid("b28f2d5a-230f-11d2-86af-00c04f8edb8a").TryWriteBytes(project.AsSpan(40, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(project.AsSpan(56), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(project.AsSpan(70), version);
        return project;
    }

    private static byte[] Zip(params (string Name, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return output.ToArray();
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static string Hex(byte[] data) => Convert.ToHexString(data).ToLowerInvariant();

    public static IEnumerable<(string Extension, byte[] Content)> SupportedSources()
    {
        yield return (".prproj", Premiere(compressed: false));
        yield return (".prproj", Premiere(compressed: true));
        yield return (".aep", Aep());
        yield return (".aepx", Aepx());
        yield return (".drp", Resolve());
        yield return (".drt", Resolve());
        yield return (".veg", Vegas());
    }
}
