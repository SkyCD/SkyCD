using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Bzip2;
using SkyCD.Plugin.FileFormat.Bzip2.Tests;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class Bzip2ArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeBzip2_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-bzip2");
        Assert.Equal(new[] { ".bz2", ".bz", ".tbz", ".tbz2" }, format.Extensions);
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-bzip2");
    }

    [Theory]
    [InlineData("catalog.bz2")]
    [InlineData("catalog.bz")]
    [InlineData("catalog.tbz")]
    [InlineData("catalog.tbz2")]
    public void GetInstanceFor_ResolvesBzip2Extensions(string fileName)
    {
        var service = CreateService();

        Assert.Equal("skycd-bzip2", service.GetInstanceFor(fileName).SupportedFormat.FormatId);
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyBzip2Format()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-bzip2",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ListsMemberWithDecodedSize()
    {
        var service = CreateService();
        await using var archive = Bzip2TestArchiveBuilder.CreateCommittedFixture();

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = archive,
            FileName = "catalog.bz2"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("file", row["kind"]);
        Assert.Equal("catalog", row["fullPath"]);
        Assert.Equal(Bzip2TestArchiveBuilder.FixtureContent.Length.ToString(), row["sizeBytes"]);
    }

    [Theory]
    [InlineData("catalog.bz2", "catalog")]
    [InlineData("catalog.bz", "catalog")]
    [InlineData("catalog.tbz", "catalog")]
    [InlineData("catalog.tbz2", "catalog")]
    public async Task ReadAsync_DerivesNameFromStorageFileName(string fileName, string expected)
    {
        await using var archive = Bzip2TestArchiveBuilder.Create("abc");

        var result = await new Bzip2ArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = archive,
            FileName = fileName
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal(expected, Assert.Single(rows)["fullPath"]);
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_ListsMember()
    {
        var service = CreateService();
        await using var archive = Bzip2TestArchiveBuilder.Create("abc");
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = nonSeekable,
            FileName = "notes.bz2"
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Equal("3", Assert.Single(rows)["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = Bzip2TestArchiveBuilder.CreateDamaged();

        var result = await new Bzip2ArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("BZIP2_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_InvalidLevel_ReportsTypedFailure()
    {
        await using var archive = Bzip2TestArchiveBuilder.CreateInvalidLevel();

        var result = await new Bzip2ArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("BZIP2_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_TruncatedArchive_ReportsTypedFailure()
    {
        await using var truncated = Bzip2TestArchiveBuilder.CreateTruncated();

        var result = await new Bzip2ArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = truncated
        });

        Assert.False(result.Success);
        Assert.StartsWith("BZIP2_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_CorruptedPayload_ReportsTypedFailure()
    {
        await using var corrupted = Bzip2TestArchiveBuilder.CreateCorruptedPayload();

        var result = await new Bzip2ArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bzip2",
            Source = corrupted
        });

        Assert.False(result.Success);
        Assert.StartsWith("BZIP2_DAMAGED", result.Error);
    }

    private static FileFormatManager CreateService() => new([new Bzip2ArchiveIndexPlugin()]);

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
