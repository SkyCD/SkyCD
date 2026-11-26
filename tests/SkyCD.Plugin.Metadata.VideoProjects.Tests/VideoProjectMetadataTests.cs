using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Metadata.VideoProjects.Tests;

public sealed class VideoProjectMetadataTests
{
    private const string Premiere = """
        <PremiereData Version="3">
          <Project ObjectID="1" Version="45"><Name>Project</Name></Project>
          <Sequence ObjectUID="seq"><Name>Edit</Name><TrackGroups><TrackGroup><Second ObjectRef="2"/></TrackGroup></TrackGroups></Sequence>
          <VideoTrackGroup ObjectID="2"><TrackGroup><Tracks><Track ObjectURef="video"/></Tracks></TrackGroup></VideoTrackGroup>
          <VideoClipTrack ObjectUID="video"/>
          <ClipProjectItem ObjectUID="item"/>
          <Media ObjectUID="media"><FilePath>missing.mov</FilePath><ActualMediaFilePath>proxy.mov</ActualMediaFilePath></Media>
        </PremiereData>
        """;

    [Fact]
    public void DescriptorUsesStableSchemaAndVerifiedFormats()
    {
        var descriptor = new VideoProjectMetadataPlugin().Descriptor;
        Assert.Equal([".prproj", ".aep", ".aepx", ".drp", ".drt", ".veg"], descriptor.Extensions);
        Assert.Equal("skycd.videoprojects.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Equal(10, descriptor.Priority);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PremiereResolvesTrackReferences(bool gzip)
    {
        var bytes = Encoding.UTF8.GetBytes(Premiere);
        var values = await Read(".prproj", gzip ? Gzip(bytes) : bytes);
        Assert.Equal("45", values["ApplicationVersion"].Text);
        Assert.Equal("Edit", values["TimelineNames"].Text);
        Assert.Equal(1, values["TimelineCount"].Integer);
        Assert.Equal(1, values["VideoTrackCount"].Integer);
        Assert.Equal(0, values["AudioTrackCount"].Integer);
        Assert.Equal(1, values["MediaFileCount"].Integer);
        Assert.Equal(1, values["ItemCount"].Integer);
    }

    [Fact]
    public async Task PremiereBoundsSequenceNames()
    {
        var sequences = Enumerable.Range(0, 40).Select(i => $"<Sequence><Name>Sequence {i}</Name></Sequence>");
        var values = await Read(".prproj", Encoding.UTF8.GetBytes(
            "<PremiereData Version=\"3\">" + string.Concat(sequences) + "</PremiereData>"));
        Assert.Equal(40, values["TimelineCount"].Integer);
        Assert.Equal(32, values["TimelineNames"].Text!.Split('\n').Length);
    }

    [Fact]
    public async Task AfterEffectsReadsTheSameFactsFromBinaryAndXml()
    {
        var composition = new byte[0xa0];
        BinaryPrimitives.WriteUInt32BigEndian(composition.AsSpan(0x2c), 120);
        BinaryPrimitives.WriteUInt32BigEndian(composition.AsSpan(0x30), 24);
        BinaryPrimitives.WriteUInt16BigEndian(composition.AsSpan(0x8c), 1920);
        BinaryPrimitives.WriteUInt16BigEndian(composition.AsSpan(0x8e), 1080);
        BinaryPrimitives.WriteUInt32BigEndian(composition.AsSpan(0x9c), 24 * 65536);

        var binary = Rifx(Chunk("head", [0, 97, 0, 7]),
            List("Fold", List("Item", Chunk("idta", [0, 4]), Chunk("Utf8", [65]), Chunk("cdta", composition)),
                List("Item", Chunk("idta", [0, 7])), List("Item", Chunk("idta", [0, 1]))));
        var xml = $"""
            <AfterEffectsProject xmlns="http://www.adobe.com/products/aftereffects" majorVersion="1" minorVersion="0">
              <head bdata="00610007"/>
              <Fold>
                <Item><idta bdata="0004"/><string>A</string><cdta bdata="{Convert.ToHexString(composition)}"/></Item>
                <Item><idta bdata="0007"/></Item>
                <Item><idta bdata="0001"/></Item>
              </Fold>
            </AfterEffectsProject>
            """;

        foreach (var values in new[] { await Read(".aep", binary), await Read(".aepx", Encoding.UTF8.GetBytes(xml)) })
        {
            Assert.Equal("97.7", values["ApplicationVersion"].Text);
            Assert.Equal(1, values["TimelineCount"].Integer);
            Assert.Equal(1, values["MediaFileCount"].Integer);
            Assert.Equal(1, values["FolderCount"].Integer);
            Assert.Equal(3, values["ItemCount"].Integer);
            Assert.Equal(24, values["FrameRate"].Number);
            Assert.Equal(5, values["Duration"].Number);
            Assert.Equal("1920 × 1080", values["Resolution"].Text);
        }
    }

    [Theory]
    [InlineData(".drp", true)]
    [InlineData(".drt", true)]
    [InlineData(".drt", false)]
    public async Task ResolveReadsTimelineAndTrackRecords(string extension, bool hasProject)
    {
        var bytes = Zip(archive =>
        {
            if (hasProject)
                Entry(archive, "project.xml", """<!--DbAppVer="21.1" DbPrjVer="17"--><SM_Project/>""");
            Entry(archive, "MediaPool/Master/MpFolder.xml", """
                <Sm2MpFolder><Sm2Timeline DbId="timeline"><Name>Resolve edit</Name>
                <Sequence><Sm2Sequence><FrameRate>0000000000003840</FrameRate><MediaExtents>00000000000000000000000000001440</MediaExtents></Sm2Sequence></Sequence>
                </Sm2Timeline></Sm2MpFolder>
                """);
            Entry(archive, "SeqContainer/12345678-1234-1234-1234-123456789abc.xml", """
                <Sm2SequenceContainer><VideoTrackVec><Element><Sm2TiTrack><Items>
                <Element><ListMgt::Sm2TiVideoClip/></Element><Element><ListMgt::Sm2TiTransition/></Element>
                </Items></Sm2TiTrack></Element></VideoTrackVec><AudioTrackVec><Sm2TiTrack/></AudioTrackVec></Sm2SequenceContainer>
                """);
            Entry(archive, "Gallery.xml", "<!DOCTYPE ignored SYSTEM \"file:///missing\"><ignored/>");
        });

        var values = await Read(extension, bytes);
        Assert.Equal(extension == ".drt", values["IsTemplate"].Boolean);
        if (hasProject)
            Assert.Equal("21.1", values["ApplicationVersion"].Text);
        Assert.Equal("Resolve edit", values["TimelineNames"].Text);
        Assert.Equal(1, values["TimelineCount"].Integer);
        Assert.Equal(2, values["TrackCount"].Integer);
        Assert.Equal(1, values["ClipCount"].Integer);
        Assert.Equal(24, values["FrameRate"].Number);
        Assert.Equal(5, values["Duration"].Number);
    }

    [Fact]
    public async Task TruncatedGzipWithIntactXmlReturnsNoMetadata()
    {
        var bytes = Gzip(Encoding.UTF8.GetBytes(Premiere));
        Assert.Empty(await Read(".prproj", bytes[..^4]));
    }

    [Fact]
    public async Task ResolveRejectsOversizedProjectEntry()
    {
        var bytes = Zip(archive =>
        {
            using var stream = archive.CreateEntry("project.xml").Open();
            stream.Write("<SM_Project>"u8);
            var block = Enumerable.Repeat((byte)' ', 1024 * 1024).ToArray();
            for (var i = 0; i < 32; i++)
                stream.Write(block);
            stream.Write("</SM_Project>"u8);
        });

        Assert.Empty(await Read(".drp", bytes));
    }

    [Fact]
    public async Task RiffChunkOverrunPreservesEarlierFacts()
    {
        var bad = Chunk("cdta", [0, 0]);
        BinaryPrimitives.WriteUInt32BigEndian(bad.AsSpan(4), uint.MaxValue);
        var values = await Read(".aep", Rifx(Chunk("head", [0, 97, 0, 7]), bad));
        Assert.Equal("97.7", values["ApplicationVersion"].Text);
        Assert.True(values["Truncated"].Boolean);
    }

    [Theory]
    [InlineData(".prproj", "<!DOCTYPE PremiereData [<!ENTITY x SYSTEM 'file:///private'>]><PremiereData Version='3'>&x;</PremiereData>")]
    [InlineData(".aepx", "<!DOCTYPE AfterEffectsProject SYSTEM 'file:///private'><AfterEffectsProject/>")]
    [InlineData(".prproj", "<NotPremiere Version='3'/>")]
    [InlineData(".aep", "RIFFxxxxWAVE")]
    public async Task RejectsDtdAndRenamedFiles(string extension, string content) =>
        Assert.Empty(await Read(extension, Encoding.UTF8.GetBytes(content)));

    [Fact]
    public async Task ResolveRejectsUnrelatedZip()
    {
        var bytes = Zip(archive => Entry(archive, "project.xml", "<NotResolve/>"));
        Assert.Empty(await Read(".drp", bytes));
        Assert.Empty(await Read(".drt", bytes));
    }

    [Fact]
    public async Task VegasRequiresProjectAndSettingsGuids()
    {
        var bytes = new byte[72];
        new Guid("66666972-912e-11cf-a5d6-28db04c10000").TryWriteBytes(bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), (ulong)bytes.Length);
        new Guid("46c429ef-904a-11d2-8722-00c04f8edb8a").TryWriteBytes(bytes.AsSpan(24));
        new Guid("b28f2d5a-230f-11d2-86af-00c04f8edb8a").TryWriteBytes(bytes.AsSpan(40));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), 32);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(70), 8);

        var values = await Read(".veg", bytes);
        Assert.Equal("8", values["ApplicationVersion"].Text);
        Assert.Empty(await Read(".vf", bytes));
        bytes[24] ^= 1;
        Assert.Empty(await Read(".veg", bytes));
    }

    private static async Task<Dictionary<string, MediaMetadataValue>> Read(string extension, byte[] bytes)
    {
        var plugin = new VideoProjectMetadataPlugin();
        var result = await plugin.ExtractAsync(new(extension, null, bytes.Length), new ByteArraySource(bytes), null, default);
        var fields = plugin.Descriptor.Schema.Fields.ToDictionary(field => field.Key);
        Assert.All(result.Values, value => Assert.Equal(fields[value.Key].Type, value.Type));
        return result.Values.ToDictionary(value => value.Key);
    }

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private static byte[] Zip(Action<ZipArchive> write)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            write(archive);
        return output.ToArray();
    }

    private static void Entry(ZipArchive archive, string name, string content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static byte[] Rifx(params byte[][] chunks) => Chunk("RIFX", [.. "Egg!"u8, .. chunks.SelectMany(chunk => chunk)]);

    private static byte[] List(string name, params byte[][] chunks) =>
        Chunk("LIST", [.. Encoding.ASCII.GetBytes(name), .. chunks.SelectMany(chunk => chunk)]);

    private static byte[] Chunk(string tag, byte[] data)
    {
        var bytes = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(tag, bytes);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4), data.Length);
        data.CopyTo(bytes, 8);
        return bytes;
    }

    private sealed class ByteArraySource(byte[] bytes) : IMediaSource
    {
        public long Length => bytes.Length;
        public long RemainingReadBytes { get; private set; } = bytes.Length;

        public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.InRange(count, 0, RemainingReadBytes);
            RemainingReadBytes -= count;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes.AsMemory((int)offset, count));
        }
    }
}
