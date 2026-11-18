using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

public sealed class ZdcReaderTests
{
    private readonly ZdcPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_SampleDatabase_ReturnsMediaPlusMembers()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);
        Assert.Equal(5, document.Rows.Count);

        var media = Assert.Single(document.Rows, r => r["kind"]?.ToString() == "media");
        Assert.Equal("catalog", media["name"]?.ToString());
        Assert.Equal(4, document.Summary.ImportedRecordsCount);
        Assert.Equal(4, document.Summary.TotalRecordsRead);
        Assert.Equal("9.x", document.Summary.DetectedVersion);

        var files = document.Rows.Where(r => r["kind"]?.ToString() == "file").ToList();
        Assert.Equal(4, files.Count);
        Assert.All(files, f => Assert.Equal(media["nodeId"], f["parentId"]));
        Assert.Contains(files, f => f["name"]?.ToString() == ZdcFormat.IndexMemberName);
        Assert.Contains(files, f => f["name"]?.ToString() == $"{ZdcFixture.DiscGuidA}.xml");
        Assert.Contains(files, f => f["name"]?.ToString() == $"{ZdcFixture.DiscGuidA}.jpg");
    }

    [Fact]
    public async Task ReadAsync_SampleDatabase_PreservesRawFields()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);

        var disc = document.Rows.First(r => r["name"]?.ToString() == $"{ZdcFixture.DiscGuidB}.xml");
        var props = Assert.IsType<Dictionary<string, object?>>(disc["properties"]);
        Assert.Equal("100", props["ZdcTimestampA"]?.ToString());
        Assert.Equal("200", props["ZdcTimestampB"]?.ToString());
        Assert.Equal("300", props["ZdcTimestampC"]?.ToString());
        Assert.Equal("1", props["ZdcFileType"]?.ToString());
        Assert.Equal("2", props["ZdcEntryType"]?.ToString());
        Assert.Equal("-1", props["ZdcParentEntry"]?.ToString());

        var media = document.Rows.First(r => r["kind"]?.ToString() == "media");
        var mediaProps = Assert.IsType<Dictionary<string, object?>>(media["properties"]);
        Assert.Equal("cdcatalogexpert-native", mediaProps["ZdcSource"]?.ToString());

        Assert.Contains("FileType=0x00000001", document.Summary.UnsupportedFields);
        Assert.Contains("EntryType=0x00000002", document.Summary.UnsupportedFields);
    }

    [Fact]
    public async Task ReadAsync_SampleDatabase_PayloadHashesMatchContent()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);

        foreach (var file in document.Rows.Where(r => r["kind"]?.ToString() == "file"))
        {
            var props = Assert.IsType<Dictionary<string, object?>>(file["properties"]);
            var payload = System.Convert.FromBase64String(props["ZdcPayload"]?.ToString() ?? string.Empty);
            var expected = System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
            Assert.Equal(expected, props["ZdcPayloadSha256"]?.ToString());
        }
    }

    [Fact]
    public void CanHandleContent_ValidContainer_ReturnsTrueAndRestoresPosition()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        Assert.True(plugin.CanHandleContent(stream, "catalog.zdc"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_BrokenXDatabase_ReturnsFalse()
    {
        var bytes = new byte[ZdcFormat.HeaderSize + 100];
        var signature = "Broken X Database\r\n\x1A"u8;
        signature.CopyTo(bytes);
        using var stream = ZdcFixture.StreamOf(bytes);
        Assert.False(plugin.CanHandleContent(stream, "catalog.zdc"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_WrongChunkSize_ReturnsFalse()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        var mutated = ZdcFixture.Mutate(bytes, copy =>
        {
            copy[ZdcFormat.ChunkSizeOffset + 1] = 0x20;
        });
        using var stream = ZdcFixture.StreamOf(mutated);
        Assert.False(plugin.CanHandleContent(stream, "catalog.zdc"));
    }

    [Fact]
    public void Read_Windows1252Xml_PreservesPayload()
    {
        var payload = Encoding.Latin1.GetBytes("<?xml version=\"1.0\" encoding=\"windows-1252\"?><catalog>\u0080</catalog>");
        var database = new ZdcDatabase();
        database.Members.Add(new ZdcMember { FileName = "catalog.xml", Payload = payload });

        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(database));
        var result = ZdcReader.Read(stream);

        Assert.Equal(payload, Assert.Single(result.Members, member => member.FileName == "catalog.xml").Payload);
    }

    [Fact]
    public void CanHandleContent_TruncatedHeader_ReturnsFalse()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        using var stream = ZdcFixture.StreamOf(bytes[..100]);
        Assert.False(plugin.CanHandleContent(stream, "catalog.zdc"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_MisalignedLength_ReturnsFalse()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        using var stream = ZdcFixture.StreamOf(bytes[..^7]);
        Assert.False(plugin.CanHandleContent(stream, "catalog.zdc"));
    }

    [Fact]
    public async Task ReadAsync_NonSeekableStream_Succeeds()
    {
        var bytes = ZdcFixture.Serialize(ZdcFixture.SampleDatabase());
        using var stream = new NonSeekableStream(new MemoryStream(bytes));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);
        Assert.Equal(5, document.Rows.Count);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new System.NotSupportedException();
        public override long Position { get => throw new System.NotSupportedException(); set => throw new System.NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new System.NotSupportedException();
        public override void SetLength(long value) => throw new System.NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new System.NotSupportedException();
    }
}
