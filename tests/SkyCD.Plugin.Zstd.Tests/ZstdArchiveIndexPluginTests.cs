using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Zstd;
using SkyCD.Plugin.Zstd.Tests;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ZstdArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeZstd_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-zstd");
        Assert.Equal(new[] { ".zst", ".tzst" }, format.Extensions);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-zstd");
    }

    [Theory]
    [InlineData("catalog.zst")]
    [InlineData("catalog.tzst")]
    [InlineData("CATALOG.ZST")]
    public void GetInstanceFor_ResolvesZstdExtensions(string fileName)
    {
        var service = CreateService();

        Assert.Equal("skycd-zstd", service.GetInstanceFor(fileName).SupportedFormat.FormatId);
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyZstdFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-zstd",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsMemberWithDecodedSize()
    {
        var service = CreateService();
        await using var archive = ZstdTestArchiveBuilder.CreateCommittedFixture();

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = archive,
            FileName = "catalog.zst"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("file", row["kind"]);
        Assert.Equal("catalog", row["fullPath"]);
        Assert.Equal(ZstdTestArchiveBuilder.FixtureContent.Length.ToString(), row["sizeBytes"]);
    }

    [Theory]
    [InlineData("catalog.zst", "catalog")]
    [InlineData("catalog.tzst", "catalog")]
    public async Task ReadAsync_DerivesNameFromStorageFileName(string fileName, string expected)
    {
        await using var archive = ZstdTestArchiveBuilder.Create("abc");

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = archive,
            FileName = fileName
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal(expected, Assert.Single(rows)["fullPath"]);
    }

    [Fact]
    public async Task ReadAsync_ConcatenatedFrames_ListsCombinedSize()
    {
        await using var archive = ZstdTestArchiveBuilder.CreateConcatenated("part-one|", "part-two");

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = archive,
            FileName = "parts.zst"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal("17", Assert.Single(rows)["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_ListsMember()
    {
        var service = CreateService();
        await using var archive = ZstdTestArchiveBuilder.Create("abc");
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = nonSeekable,
            FileName = "notes.zst"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal("3", Assert.Single(rows)["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = ZstdTestArchiveBuilder.CreateDamaged();

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("ZSTD_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_SkippableFrame_ReportsUnsupported()
    {
        await using var archive = ZstdTestArchiveBuilder.CreateSkippable();

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("ZSTD_UNSUPPORTED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_ReservedDescriptorBits_ReportsUnsupported()
    {
        await using var archive = ZstdTestArchiveBuilder.CreateUnsupportedFlags();

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("ZSTD_UNSUPPORTED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_TruncatedArchive_ReportsTypedFailure()
    {
        await using var truncated = ZstdTestArchiveBuilder.CreateTruncated();

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = truncated
        });

        Assert.False(result.Success);
        Assert.StartsWith("ZSTD_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_CorruptedPayload_ReportsTypedFailure()
    {
        await using var corrupted = ZstdTestArchiveBuilder.CreateCorruptedPayload();

        var result = await new ZstdArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zstd",
            Source = corrupted
        });

        Assert.False(result.Success);
        Assert.StartsWith("ZSTD_DAMAGED", result.Error);
    }

    private static FileFormatManager CreateService() => new([new ZstdArchiveIndexPlugin()]);

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
