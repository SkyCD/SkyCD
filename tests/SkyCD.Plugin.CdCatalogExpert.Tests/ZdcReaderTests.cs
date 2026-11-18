using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.CdCatalogExpert;
using Xunit;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

public sealed class ZdcReaderTests
{
    private readonly ZdcPlugin plugin = new();

    [Fact]
    public async Task ReadAsync_SampleDatabase_RebuildsCatalogHierarchy()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);
        Assert.Equal(8, document.Rows.Count);

        var catalog = Assert.Single(document.Rows, r => r["properties"] is Dictionary<string, object?> p &&
            p.TryGetValue("ZdcNodeKind", out var kind) && kind?.ToString() == "catalog");
        Assert.Equal("Sample Catalog", catalog["name"]?.ToString());
        Assert.Equal("folder", catalog["kind"]?.ToString());
        Assert.Null(catalog["parentId"]);

        var groups = document.Rows.Where(r => r["kind"]?.ToString() == "folder" && r["parentId"] is not null).ToList();
        var games = Assert.Single(groups, g => g["name"]?.ToString() == "Games");
        Assert.Equal(catalog["nodeId"], games["parentId"]);

        var media = document.Rows.Where(r => r["kind"]?.ToString() == "media").ToList();
        Assert.Equal(2, media.Count);
        Assert.All(media, m => Assert.Equal(games["nodeId"], m["parentId"]));

        var files = document.Rows.Where(r => r["kind"]?.ToString() == "file").ToList();
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f["name"]?.ToString() == "readme.txt");
        Assert.Contains(files, f => f["name"]?.ToString() == "file-6ba7b810.txt");
        Assert.Single(document.Rows, r => r["name"]?.ToString() == "Empty" && r["kind"]?.ToString() == "folder");

        Assert.Equal(7, document.Summary.ImportedRecordsCount);
        Assert.Equal(7, document.Summary.TotalRecordsRead);
        Assert.Equal("9.x", document.Summary.DetectedVersion);
    }

    [Fact]
    public async Task ReadAsync_SampleDatabase_PreservesRawDiscFields()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);

        var disc = document.Rows.First(r => r["name"]?.ToString() == "Disc A");
        var props = Assert.IsType<Dictionary<string, object?>>(disc["properties"]);
        Assert.Equal("{3f2504e0-4f89-11d3-9a0c-0305e82c3301}", props["ZdcDiscGuid"]?.ToString());
        Assert.Equal("D:\\", props["ZdcSourcePath"]?.ToString());
        Assert.Equal("1700000001", props["ZdcCreated"]?.ToString());
        Assert.Equal("1700000002", props["ZdcModified"]?.ToString());
        Assert.Equal("DISCA", props["ZdcVolumeLabel"]?.ToString());
        Assert.Equal("CDFS", props["ZdcFileSystem"]?.ToString());
        Assert.Equal("2", props["ZdcMediaType"]?.ToString());
        Assert.Equal("-123456", props["ZdcVolumeSerial"]?.ToString());
        Assert.Equal("700000000", props["ZdcCapacity"]?.ToString());
        Assert.Equal("1000", props["ZdcFreeSpace"]?.ToString());
        Assert.Equal("1", props["ZdcOptions"]?.ToString());
        Assert.Equal("1", props["ZdcScanFlagA"]?.ToString());
        Assert.Equal("0", props["ZdcScanFlagB"]?.ToString());
        Assert.Equal("1", props["ZdcScanFlagG"]?.ToString());
        Assert.Equal("games, retro", props["ZdcKeywords"]?.ToString());
        Assert.Equal("Shelf 1", props["ZdcStorageLocation"]?.ToString());
        Assert.Equal("IDX-1", props["ZdcIndexCode"]?.ToString());
        Assert.Equal("0001234567890", props["ZdcBarcode"]?.ToString());
        Assert.Equal("5", props["ZdcRating"]?.ToString());
        Assert.Equal("disc", props["ZdcNodeKind"]?.ToString());

        var unavailable = document.Rows.First(r => r["name"]?.ToString() == "Disc B");
        var unavailableProps = Assert.IsType<Dictionary<string, object?>>(unavailable["properties"]);
        Assert.Equal("-1", unavailableProps["ZdcCapacity"]?.ToString());
        Assert.Equal("-1", unavailableProps["ZdcFreeSpace"]?.ToString());

        Assert.Contains(document.Summary.UnsupportedFields, f => f == "FileType=0x00000001");
        Assert.Contains(document.Summary.UnsupportedFields, f => f == "EntryType=0x00000002");
    }

    [Fact]
    public async Task ReadAsync_SampleDatabase_PreservesTreeFieldsAndCover()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);

        var readme = document.Rows.First(r => r["name"]?.ToString() == "readme.txt");
        Assert.Equal("5", readme["sizeBytes"]?.ToString());
        var treeProps = Assert.IsType<Dictionary<string, object?>>(readme["properties"]);
        Assert.Equal("32", treeProps["ZdcAttributes"]?.ToString());
        Assert.Equal("Read me", treeProps["ZdcDescription"]?.ToString());
        Assert.Equal("file", treeProps["ZdcNodeKind"]?.ToString());

        var folder = document.Rows.First(r => r["name"]?.ToString() == "Folder");
        var folderProps = Assert.IsType<Dictionary<string, object?>>(folder["properties"]);
        Assert.Equal("16", folderProps["ZdcAttributes"]?.ToString());
        Assert.Equal("1700000005", folderProps["ZdcTimestampB"]?.ToString());
        Assert.Equal("directory", folderProps["ZdcNodeKind"]?.ToString());

        var disc = document.Rows.First(r => r["name"]?.ToString() == "Disc A");
        var discProps = Assert.IsType<Dictionary<string, object?>>(disc["properties"]);
        var cover = System.Convert.FromBase64String(discProps["ZdcCover"]?.ToString() ?? string.Empty);
        Assert.Equal(ZdcFixture.MinimalJpeg(), cover);
        Assert.Equal("3f2504e0-4f89-11d3-9a0c-0305e82c3301.jpg", discProps["ZdcCoverMember"]?.ToString());
    }

    [Fact]
    public void CanHandleContent_ValidContainer_ReturnsTrueAndRestoresPosition()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        Assert.True(plugin.CanHandleContent(stream, "catalog.zdc"));
        Assert.Equal(0, stream.Position);
        Assert.True(plugin.CanHandleContent(stream, null));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_NonZdcExtension_ReturnsFalse()
    {
        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(ZdcFixture.SampleDatabase()));
        Assert.False(plugin.CanHandleContent(stream, "catalog.xml"));
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
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);
        Assert.Equal(8, document.Rows.Count);
    }

    [Fact]
    public async Task ReadAsync_MissingDiscTree_ReturnsFormatFailure()
    {
        var database = new ZdcDatabase
        {
            Members =
            {
                ZdcFixture.IndexMember(ZdcFixture.IndexXml(ZdcFixture.DiscGuidA))
            }
        };

        using var stream = ZdcFixture.StreamOf(ZdcFixture.Serialize(database));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream));
        Assert.False(result.Success);
        Assert.Contains("missing disc-tree member", result.Error);
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
