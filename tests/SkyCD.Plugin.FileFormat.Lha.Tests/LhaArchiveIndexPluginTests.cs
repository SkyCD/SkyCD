using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Lha;
using SkyCD.Plugin.FileFormat.Lha.Tests;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class LhaArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeLha_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-lha");
        Assert.Equal(new[] { ".lha", ".lzh" }, format.Extensions);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-lha");
    }

    [Theory]
    [InlineData("catalog.lha")]
    [InlineData("catalog.lzh")]
    public void GetInstanceFor_ResolvesBothLhaExtensions(string fileName)
    {
        var service = CreateService();

        Assert.Equal("skycd-lha", service.GetInstanceFor(fileName).SupportedFormat.FormatId);
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyLhaFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-lha",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesNestedPathsAndMetadata()
    {
        var service = CreateService();
        await using var archive = LhaTestArchiveBuilder.CreateCompressed(
            ("root/notes.txt", "abc"),
            ("root/deep/leaf.txt", new string('x', 4096)));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-lha",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root"));
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root/deep"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "root/deep/leaf.txt")
            && Equals(row["sizeBytes"], "4096"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/notes.txt") && row.ContainsKey("modifiedUtc"));
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_ListsEntries()
    {
        var service = CreateService();
        await using var archive = LhaTestArchiveBuilder.CreateStored(("root/notes.txt", "abc"));
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-lha",
            Source = nonSeekable
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/notes.txt"));
    }

    [Fact]
    public async Task ReadAsync_CommittedFixture_ListsEntries()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Lha", "catalog.lha");
        await using var stream = new FileStream(fixturePath, FileMode.Open);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-lha",
            Source = stream
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/readme.txt"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/data/track.bin"));
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = LhaTestArchiveBuilder.CreateDamaged();

        var result = await new LhaArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-lha",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("LHA_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedMethod_StillListsEntries()
    {
        await using var archive = LhaTestArchiveBuilder.CreateUnsupportedMethod(("stored.bin", "abc"));

        var result = await new LhaArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-lha",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["kind"], "file") && Equals(row["fullPath"], "stored.bin"));
    }

    private static FileFormatManager CreateService() => new([new LhaArchiveIndexPlugin()]);

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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
