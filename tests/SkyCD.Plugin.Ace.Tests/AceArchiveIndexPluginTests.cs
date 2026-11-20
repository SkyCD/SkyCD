using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Ace;
using SkyCD.Plugin.Ace.Tests;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class AceArchiveIndexPluginTests
{
    [Fact]
    public void Descriptor_DeclaresReadOnlyAceFormat()
    {
        var format = new AceArchiveIndexPlugin().SupportedFormat;

        Assert.Equal("skycd-ace", format.FormatId);
        Assert.Equal("ACE Archive Index", format.DisplayName);
        Assert.Equal([".ace"], format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
        Assert.Contains(format.MimeTypes, mime =>
            mime.Contains("ace", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenFormats_IncludeAce_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        var format = Assert.Single(openFormats, candidate => candidate.FormatId == "skycd-ace");
        Assert.Equal(".ace", Assert.Single(format.Extensions));
        Assert.DoesNotContain(saveFormats, candidate => candidate.FormatId == "skycd-ace");
    }

    [Theory]
    [InlineData("catalog.ace")]
    [InlineData("CATALOG.ACE")]
    public void GetInstanceFor_ResolvesAceExtension(string fileName)
    {
        Assert.Equal("skycd-ace", CreateService().GetInstanceFor(fileName).SupportedFormat.FormatId);
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyAceFormat()
    {
        var service = CreateService();
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-ace",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesNestedPathsAndMetadata()
    {
        var service = CreateService();
        await using var archive = AceTestArchiveBuilder.CreateStored(
            ("root/notes.txt", "abc"),
            ("root/deep/leaf.txt", "hello"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root"));
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "root/deep"));
        Assert.Contains(rows, row =>
            Equals(row["kind"], "file")
            && Equals(row["fullPath"], "root/deep/leaf.txt")
            && Equals(row["sizeBytes"], "5"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/leaf.txt")
                                     && row["modifiedUtc"] is string value
                                     && value.Length > 0);
    }

    [Fact]
    public async Task ReadAsync_CompressedAndEncryptedEntries_AreListed()
    {
        var service = CreateService();
        await using var archive = AceTestArchiveBuilder.CreateCompressed(method: 7, ("compressed.bin", "abc"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var row = Assert.Single(rows);
        Assert.Equal("file", row["kind"]);
        Assert.Equal("compressed.bin", row["fullPath"]);
        Assert.Equal("3", row["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_NestedFolderEntry_IsListedAsFolder()
    {
        var service = CreateService();
        await using var archive = AceTestArchiveBuilder.CreateStored(("folder/", string.Empty), ("folder/a.txt", "a"));

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Single(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "folder"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "folder/a.txt"));
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_ListsEntries()
    {
        var service = CreateService();
        await using var archive = AceTestArchiveBuilder.CreateStored(("root/notes.txt", "abc"));
        await using var nonSeekable = new NonSeekableStream(archive);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
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
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Ace", "catalog.ace");
        await using var stream = new FileStream(fixturePath, FileMode.Open);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = stream
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/readme.txt"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "catalog/data/track.bin"));
    }

    [Theory]
    [InlineData("plain text that is not an archive")]
    [InlineData("")]
    public async Task ReadAsync_NonAceFile_ReportsDamagedInsteadOfEmptyArchive(string content)
    {
        await using var source = new MemoryStream(Encoding.ASCII.GetBytes(content));

        var result = await new AceArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = source
        });

        Assert.False(result.Success);
        Assert.StartsWith("ACE_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_DamagedArchive_ReportsTypedFailure()
    {
        await using var broken = AceTestArchiveBuilder.CreateDamaged();

        var result = await new AceArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = broken
        });

        Assert.False(result.Success);
        Assert.StartsWith("ACE_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_TruncatedArchive_ReportsTypedFailure()
    {
        await using var truncated = AceTestArchiveBuilder.CreateTruncated(("cut.txt", "0123456789"));

        var result = await new AceArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = truncated
        });

        Assert.False(result.Success);
        Assert.StartsWith("ACE_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_BadHeaderChecksum_ReportsTypedFailure()
    {
        await using var archive = AceTestArchiveBuilder.CreateStoredWithBadHeaderChecksum(("bad.txt", "abc"));

        var result = await new AceArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("ACE_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_Ace1Archive_ReportsUnsupportedVersion()
    {
        // An ACE 1.0 main header starts with 0x14 instead of the ACE 2.0 0x24 header type.
        var bytes = AceTestArchiveBuilder.ToBytes(AceTestArchiveBuilder.CreateStored(("a.txt", "a")));
        bytes[7] = 0x14;
        await using var source = new MemoryStream(bytes);

        var result = await new AceArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-ace",
            Source = source
        });

        Assert.False(result.Success);
        Assert.StartsWith("ACE_DAMAGED", result.Error);
    }

    private static FileFormatManager CreateService() => new([new AceArchiveIndexPlugin()]);

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
