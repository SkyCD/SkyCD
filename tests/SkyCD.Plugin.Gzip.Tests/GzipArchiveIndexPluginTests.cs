using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Gzip;
using SkyCD.Plugin.Gzip.Tests;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Tar;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class GzipArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeGzip_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-gzip");
        Assert.Equal(new[] { ".gz", ".gzip" }, format.Extensions);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-gzip");
    }

    [Theory]
    [InlineData("catalog.gz")]
    [InlineData("catalog.gzip")]
    public void GetInstanceFor_ResolvesBothGzipExtensions(string fileName)
    {
        var service = CreateService();

        Assert.Equal("skycd-gzip", service.GetInstanceFor(fileName).SupportedFormat.FormatId);
    }

    [Fact]
    public void GetInstanceFor_PrefersTarForCompressedTarballs()
    {
        var service = new FileFormatManager(
        [
            new GzipArchiveIndexPlugin(),
            new TarArchiveIndexPlugin()
        ]);

        Assert.Equal("skycd-tar", service.GetInstanceFor("catalog.tar.gz").SupportedFormat.FormatId);
        Assert.Equal("skycd-gzip", service.GetInstanceFor("catalog.gz").SupportedFormat.FormatId);
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyGzipFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-gzip",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsNamedMemberWithSizeAndTimestamp()
    {
        var service = CreateService();
        await using var archive = GzipTestArchiveBuilder.Create("abc", "catalog/readme.txt",
            GzipTestArchiveBuilder.ModifiedUtc);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = archive,
            FileName = "catalog.gz"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("file", row["kind"]);
        Assert.Equal("catalog/readme.txt", row["fullPath"]);
        Assert.Equal("readme.txt", row["name"]);
        Assert.Equal("3", row["sizeBytes"]);
        Assert.StartsWith("2024-01-02T03:04:06", row["modifiedUtc"]?.ToString());
    }

    [Fact]
    public async Task ReadAsync_WithoutHeaderName_UsesStorageFileName()
    {
        var service = CreateService();
        await using var archive = GzipTestArchiveBuilder.Create("abc");

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = archive,
            FileName = "notes.gzip"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("notes", row["fullPath"]);
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_ListsMember()
    {
        var service = CreateService();
        await using var archive = GzipTestArchiveBuilder.Create("abc", "readme.txt");
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = nonSeekable
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("readme.txt", row["fullPath"]);
        Assert.Equal("3", row["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = GzipTestArchiveBuilder.CreateDamaged();

        var result = await new GzipArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("GZIP_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_TruncatedArchive_ReportsTypedFailure()
    {
        await using var truncated = GzipTestArchiveBuilder.CreateTruncated("abc");

        var result = await new GzipArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = truncated
        });

        Assert.False(result.Success);
        Assert.StartsWith("GZIP_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedMethod_ReportsTypedFailure()
    {
        await using var archive = GzipTestArchiveBuilder.CreateUnsupportedMethod("abc");

        var result = await new GzipArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("GZIP_UNSUPPORTED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_CommittedFixture_ListsMember()
    {
        var service = CreateService();
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Gzip", "catalog.gz");
        await using var stream = new FileStream(fixturePath, FileMode.Open);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-gzip",
            Source = stream,
            FileName = "catalog.gz"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.False(string.IsNullOrWhiteSpace(row["name"]?.ToString()));
        Assert.NotEqual("0", row["sizeBytes"]);
    }

    private static FileFormatManager CreateService() => new([new GzipArchiveIndexPlugin()]);

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
