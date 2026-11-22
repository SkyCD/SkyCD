using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.CDCollect.Exceptions;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.FileFormat.CDCollect.Models;
using SkyCD.Plugin.FileFormat.CDCollect.Parsing;

namespace SkyCD.Plugin.FileFormat.CDCollect.Tests;

public sealed class CdCollectSqlite2Tests
{
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Sqlite2", name);

    [Theory]
    [InlineData("cds.db")]
    [InlineData("big-endian.db")]
    [InlineData("reordered.db")]
    [InlineData("version0.db")]
    [InlineData("files-v1-v4.db")]
    public async Task Import_RealSqlite28Fixture_PreservesHierarchyAndMetadata(string name)
    {
        var path = Fixture(name);
        using var stream = File.OpenRead(path);
        var plugin = new CdCollectSqlite2Plugin();
        Assert.True(plugin.CanHandleContent(stream, "anything.bin"));
        Assert.Equal(0, stream.Position);
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        { Source = stream, FormatId = "cdcollect-sqlite2", SourcePath = path });
        Assert.True(result.Success, result.Error);
        var report = Assert.IsType<CdCollectReportDocument>(result.Payload);
        Assert.Equal(2, report.Summary.MediaCount);
        Assert.Equal(1, report.Summary.DirectoryCount);
        Assert.Equal(3, report.Summary.FileCount);
        var media = report.Single(r => Equals(r["Name"], "Legacy music"));
        var properties = (Dictionary<string, object?>)media["Properties"]!;
        Assert.Equal("2005-03-15", properties["Date"]);
        Assert.Equal("rock, backups", properties["Keywords"]);
        Assert.Equal("cdrom", properties["Icon"]);
        var folder = report.Single(r => Equals(r["Name"], "Music"));
        var file = report.Single(r => Equals(r["Name"], "O'Brien.mp3"));
        Assert.Equal("File", file["Type"]);
        Assert.Equal(folder["Id"], file["ParentId"]);
        Assert.Equal(123456L, file["Size"]);
        var fileProperties = (Dictionary<string, object?>)file["Properties"]!;
        Assert.Equal("Artist: Queen", fileProperties["Info"]);
        Assert.Equal("Queen", fileProperties["Artist"]);
        Assert.Equal("audio/mpeg", fileProperties["MimeType"]);
        var archive = report.Single(r => Equals(r["Name"], "archive.zip"));
        var child = report.Single(r => Equals(r["Name"], "readme.txt"));
        Assert.Equal(archive["Id"], child["ParentId"]);
        Assert.Equal("true", ((Dictionary<string, object?>)archive["Properties"]!)["IsPacked"]);
        Assert.Equal("  keep whitespace  ", ((Dictionary<string, object?>)child["Properties"]!)["Info"]);
    }

    [Fact]
    public void Import_MultiPageBtreeAndOverflowRecords_PreservesEveryRecord()
    {
        using var stream = File.OpenRead(Fixture("large.db"));
        var report = CdCollectSqlite2Parser.Parse(stream);
        Assert.Equal(143, report.Summary.FileCount);
        Assert.Equal(2, report.Summary.DirectoryCount);
        var file = report.Single(r => Equals(r["Name"], "file20.txt"));
        Assert.Equal(70000, ((Dictionary<string, object?>)file["Properties"]!)["Info"]?.ToString()?.Length);
        var medium = report.Single(r => Equals(r["Name"], "file21.txt"));
        Assert.Equal(1100, ((Dictionary<string, object?>)medium["Properties"]!)["Info"]?.ToString()?.Length);
    }

    [Theory]
    [InlineData("files-v1-v1.db")]
    [InlineData("unsupported-columns.db")]
    public void Import_UnsupportedHistoricalVariant_ReportsMigrationPath(string name)
    {
        using var stream = File.OpenRead(Fixture(name));
        var error = Assert.Throws<CdCollectSqlite2VersionException>(() => CdCollectSqlite2Parser.Parse(stream));
        Assert.Contains("migration", error.Message);
    }

    [Fact]
    public async Task Import_ShortReadsWithoutSeeking_Succeeds()
    {
        using var stream = new ChunkedStream(File.ReadAllBytes(Fixture("cds.db")));
        var result = await new CdCollectSqlite2Plugin().ReadAsync(new FileFormatReadRequest
        { Source = stream, FormatId = "cdcollect-sqlite2" });
        Assert.True(result.Success, result.Error);
        Assert.Equal(6, Assert.IsType<CdCollectReportDocument>(result.Payload).Count);
    }

    [Fact]
    public void Detection_PreservesNonzeroPosition()
    {
        using var stream = File.OpenRead(Fixture("cds.db"));
        stream.Position = 17;
        Assert.True(new CdCollectSqlite2Plugin().CanHandleContent(stream));
        Assert.Equal(17, stream.Position);
    }

    [Fact]
    public void Import_UnknownVersion_ThrowsDedicatedActionableError()
    {
        using var stream = File.OpenRead(Fixture("unknown-version.db"));
        Assert.True(new CdCollectSqlite2Plugin().CanHandleContent(stream));
        var error = Assert.Throws<CdCollectSqlite2VersionException>(() => CdCollectSqlite2Parser.Parse(stream));
        Assert.Contains("99", error.Message);
        Assert.Contains("cds3.db", error.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    [InlineData(1024)]
    [InlineData(5119)]
    public async Task Import_TruncatedInput_IsRejected(int length)
    {
        var bytes = File.ReadAllBytes(Fixture("cds.db"))[..length];
        using var source = new MemoryStream(bytes);
        var plugin = new CdCollectSqlite2Plugin();
        Assert.False(plugin.CanHandleContent(source, "cds.db"));
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        { Source = source, FormatId = "cdcollect-sqlite2" });
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error!);
    }

    [Fact]
    public void Import_CyclicPageReference_IsRejected()
    {
        var bytes = File.ReadAllBytes(Fixture("cds.db"));
        // sqlite_master's right child points back to itself.
        bytes[1024] = 2;
        using var stream = new MemoryStream(bytes);
        Assert.Throws<CdCollectSqlite2CorruptException>(() => CdCollectSqlite2Parser.Parse(stream));
    }

    [Fact]
    public void Import_CyclicCellLink_IsRejected()
    {
        var bytes = File.ReadAllBytes(Fixture("cds.db"));
        var first = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1028, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1024 + first + 6, 2), first);
        using var stream = new MemoryStream(bytes);
        Assert.Throws<CdCollectSqlite2CorruptException>(() => CdCollectSqlite2Parser.Parse(stream));
    }

    [Fact]
    public void Import_CyclicOverflowChain_IsRejected()
    {
        var bytes = File.ReadAllBytes(Fixture("large.db"));
        // Find a fixture overflow page whose payload is entirely 'x', then loop its next-page pointer.
        var offset = Enumerable.Range(1, bytes.Length / 1024 - 1).Select(i => i * 1024)
            .First(i => bytes.AsSpan(i + 4, 96).SequenceEqual(Enumerable.Repeat((byte)'x', 96).ToArray()));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), (uint)(offset / 1024 + 1));
        using var stream = new MemoryStream(bytes);
        Assert.Throws<CdCollectSqlite2CorruptException>(() => CdCollectSqlite2Parser.Parse(stream));
    }

    [Fact]
    public void Import_UnknownStorageFormat_ReportsActionableVersionError()
    {
        var bytes = File.ReadAllBytes(Fixture("cds.db"));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(64, 4), 99);
        using var stream = new MemoryStream(bytes);
        var error = Assert.Throws<CdCollectSqlite2VersionException>(() => CdCollectSqlite2Parser.Parse(stream));
        Assert.Contains("storage format 99", error.Message);
    }

    [Theory]
    [InlineData("other.db")]
    [InlineData("../cds3.db")]
    public async Task Import_UnrelatedDatabase_IsRejectedRegardlessOfFilename(string name)
    {
        using var stream = File.OpenRead(Fixture(name));
        var plugin = new CdCollectSqlite2Plugin();
        Assert.False(plugin.CanHandleContent(stream, "cds.db"));
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        { Source = stream, FormatId = "cdcollect-sqlite2" });
        Assert.False(result.Success);
    }

    [Fact]
    public async Task DiscoveryAndRouting_AdvertiseReadOnly_AndExcludeLegacyFromSaveChoices()
    {
        var discovered = new DiscoveredPluginFactory().BuildFromAssembly(typeof(CdCollectSqlite2Plugin).Assembly);
        var capabilities = discovered.Capabilities.OfType<IFileFormatPluginCapability>().ToArray();
        Assert.Contains(capabilities, c => c is CdCollectSqlite2Plugin);
        var manager = new FileFormatManager(capabilities);
        Assert.Contains(manager.GetOpenFormats(), f => f.FormatId == "cdcollect-sqlite2");
        Assert.DoesNotContain(manager.GetSaveFormats(), f => f.FormatId == "cdcollect-sqlite2");
        Assert.Equal("cdcollect-sqlite2", manager.ResolveFormatId(null, Fixture("cds.db"), false));
        Assert.Equal("cdcollect-db", manager.ResolveFormatId(null, Fixture("../cds3.db"), false));
        using var output = new MemoryStream();
        var write = await new CdCollectSqlite2Plugin().WriteAsync(new FileFormatWriteRequest
        { Target = output, Payload = Array.Empty<object>(), FormatId = "cdcollect-sqlite2" });
        Assert.False(write.Success);
        Assert.Equal(0, output.Length);
    }

    [Fact]
    public async Task Import_Cancellation_Propagates()
    {
        using var stream = File.OpenRead(Fixture("cds.db"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CdCollectSqlite2Plugin().ReadAsync(
            new FileFormatReadRequest { Source = stream, FormatId = "cdcollect-sqlite2" }, new CancellationToken(true)));
    }
    private sealed class ChunkedStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream inner = new(bytes);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, 7));
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }

}
