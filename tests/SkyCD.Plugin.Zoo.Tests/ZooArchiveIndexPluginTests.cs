using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Zoo;
using SkyCD.Plugin.Zoo.Tests;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ZooArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeZoo_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-zoo" && format.Extensions.Contains(".zoo"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-zoo");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyZooFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-zoo",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesNestedPathsAndMetadata()
    {
        var service = CreateService();
        await using var archive = ZooTestArchiveBuilder.CreateStored(
            ("root/notes.txt", "abc"),
            ("root/deep/leaf.txt", "hello"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zoo",
            Source = archive
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root"));
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root/deep"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "root/deep/leaf.txt")
            && Equals(row["sizeBytes"], "5"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/leaf.txt") && row.ContainsKey("modifiedUtc"));
    }

    [Fact]
    public async Task ReadAsync_ListsEntriesWithUnsupportedPackingMethod()
    {
        await using var archive = ZooTestArchiveBuilder.CreateUnsupported(("compressed.txt", "payload"));

        var result = await new ZooArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zoo",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "compressed.txt"));
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var damaged = ZooTestArchiveBuilder.CreateDamaged();

        var result = await new ZooArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zoo",
            Source = damaged
        });

        Assert.False(result.Success);
        Assert.StartsWith("ZOO_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_IsBuffered()
    {
        using var archive = ZooTestArchiveBuilder.CreateStored(("bucket/file.txt", "content"));
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await new ZooArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zoo",
            Source = nonSeekable
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "bucket/file.txt"));
    }

    [Fact]
    public async Task ReadAsync_CommittedFixture_ListsEntries()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Zoo", "catalog.zoo");
        await using var stream = new FileStream(fixturePath, FileMode.Open);

        var result = await new ZooArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zoo",
            Source = stream
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/readme.txt"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/data/track.bin"));
    }

    [Fact]
    public async Task ReadAsync_SkipsUnsafeEntries()
    {
        await using var archive = ZooTestArchiveBuilder.CreateStored(
            ("../escape.txt", "evil"),
            ("C:/windows/evil.txt", "evil"),
            ("folder/nested.txt", "nested"));

        var result = await new ZooArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zoo",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "folder/nested.txt"));
        Assert.DoesNotContain(rows, row => Equals(row["fullPath"], "../escape.txt"));
        Assert.DoesNotContain(rows, row => Equals(row["fullPath"], "C:/windows/evil.txt"));
    }

    private static FileFormatManager CreateService() => new([new ZooArchiveIndexPlugin()]);

    /// <summary>Hides seeking so the plugin has to buffer the source itself.</summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
