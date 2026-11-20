using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Xz;
using SkyCD.Plugin.Xz.Tests;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class XzArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeXz_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-xz");
        Assert.Equal(new[] { ".xz", ".txz" }, format.Extensions);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-xz");
    }

    [Theory]
    [InlineData("catalog.xz")]
    [InlineData("catalog.txz")]
    public void GetInstanceFor_ResolvesBothXzExtensions(string fileName)
    {
        var service = CreateService();

        Assert.Equal("skycd-xz", service.GetInstanceFor(fileName).SupportedFormat.FormatId);
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyXzFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-xz",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsMemberWithDecodedSize()
    {
        var service = CreateService();
        await using var archive = XzTestArchiveBuilder.CreateValid();

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = archive,
            FileName = "catalog.xz"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("file", row["kind"]);
        Assert.Equal("catalog", row["fullPath"]);
        Assert.Equal(XzTestArchiveBuilder.FixtureContent.Length.ToString(), row["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_TxzExtension_DropsOnlyTheCompressionSuffix()
    {
        await using var archive = XzTestArchiveBuilder.CreateValid();

        var result = await new XzArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = archive,
            FileName = "catalog.txz"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal("catalog", Assert.Single(rows)["fullPath"]);
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_ListsMember()
    {
        var service = CreateService();
        await using var archive = XzTestArchiveBuilder.CreateValid();
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = nonSeekable,
            FileName = "catalog.xz"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal("catalog", Assert.Single(rows)["fullPath"]);
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = XzTestArchiveBuilder.CreateDamaged();

        var result = await new XzArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("XZ_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_TruncatedArchive_ReportsTypedFailure()
    {
        await using var truncated = XzTestArchiveBuilder.CreateTruncated();

        var result = await new XzArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = truncated
        });

        Assert.False(result.Success);
        Assert.StartsWith("XZ_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_CorruptedPayload_ReportsTypedFailure()
    {
        await using var corrupted = XzTestArchiveBuilder.CreateCorruptedPayload();

        var result = await new XzArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = corrupted
        });

        Assert.False(result.Success);
        Assert.StartsWith("XZ_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_UnsupportedStreamFlags_ReportsTypedFailure()
    {
        await using var archive = XzTestArchiveBuilder.CreateUnsupportedFlags();

        var result = await new XzArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-xz",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("XZ_UNSUPPORTED", result.Error);
    }

    private static FileFormatManager CreateService() => new([new XzArchiveIndexPlugin()]);

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
