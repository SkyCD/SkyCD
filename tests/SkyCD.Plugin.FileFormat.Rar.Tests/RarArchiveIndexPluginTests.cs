using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Rar;
using SkyCD.Plugin.FileFormat.Rar.Tests;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class RarArchiveIndexPluginTests
{
    [Fact]
    public void Descriptor_IsReadOnlyRarFormat()
    {
        var descriptor = new RarArchiveIndexPlugin().SupportedFormat;

        Assert.Equal("skycd-rar", descriptor.FormatId);
        Assert.Equal(".rar", Assert.Single(descriptor.Extensions));
        Assert.True(descriptor.CanRead);
        Assert.False(descriptor.CanWrite);
        Assert.Contains("application/vnd.rar", descriptor.MimeTypes);
    }

    [Fact]
    public void OpenFormats_IncludeRar_ButSaveFormatsDoNot()
    {
        var manager = new FileFormatManager([new RarArchiveIndexPlugin()]);

        Assert.Contains(manager.GetOpenFormats(), format => format.FormatId == "skycd-rar");
        Assert.DoesNotContain(manager.GetSaveFormats(), format => format.FormatId == "skycd-rar");
    }

    [Theory]
    [InlineData("catalog.rar")]
    [InlineData("CATALOG.RAR")]
    public void GetInstanceFor_ResolvesRarExtension(string fileName)
    {
        var manager = new FileFormatManager([new RarArchiveIndexPlugin()]);

        Assert.Equal("skycd-rar", manager.GetInstanceFor(fileName).SupportedFormat.FormatId);
        Assert.Equal("skycd-rar", manager.ResolveFormatId(null, fileName, forWrite: false));
    }

    [Fact]
    public void GetInstanceFor_DoesNotClaimOtherArchiveExtensions()
    {
        var manager = new FileFormatManager([new RarArchiveIndexPlugin()]);

        Assert.Throws<UnsupportedFileFormatException>(() =>
            manager.ResolveFormatId(null, "catalog.zip", forWrite: false));
        Assert.Throws<UnsupportedFileFormatException>(() =>
            manager.ResolveFormatId(null, "catalog.r00", forWrite: false));
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyRarFormat()
    {
        var manager = new FileFormatManager([new RarArchiveIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            manager.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-rar",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_RejectsNonRarInput_AsDamagedInsteadOfEmptyArchive()
    {
        await using var source = new MemoryStream(Encoding.UTF8.GetBytes("plain text, not an archive"));

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = source,
            FileName = "notes.rar"
        });

        Assert.False(result.Success);
        Assert.StartsWith("RAR_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_RejectsSignatureOnlyTruncation()
    {
        await using var source = new MemoryStream(RarTestArchiveBuilder.CreateStored(("a.txt", "content")).ToArray()[..6]);

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = source
        });

        Assert.False(result.Success);
        Assert.StartsWith("RAR_DAMAGED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_ListsNestedFoldersAndFiles()
    {
        await using var archive = RarTestArchiveBuilder.CreateStored(
            ("root/notes.txt", "abc"),
            ("root/deep/leaf.txt", "hello"),
            ("root/empty.bin", string.Empty));

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
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
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/empty.bin") && Equals(row["sizeBytes"], "0"));
    }

    [Fact]
    public async Task ReadAsync_ReportsStoredEntriesExactlyOnce_WhenQuickOpenServiceHeadersPresent()
    {
        await using var archive = RarTestArchiveBuilder.CreateWithServiceHeaders(
            ("folder/data.txt", "payload"));

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = archive
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        var file = Assert.Single(rows, row => Equals(row["kind"], "file"));
        Assert.Equal("folder/data.txt", file["fullPath"]);
        Assert.Equal("7", file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_NormalizesSeparatorsAndReportsProgress()
    {
        await using var archive = RarTestArchiveBuilder.CreateStored(("My Folder/My Track.bin", "1234"));
        var progress = new List<int>();

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = archive,
            FileName = "music.rar",
            Progress = new Progress<int>(progress.Add)
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "My Folder"));
        Assert.Contains(rows, row => Equals(row["kind"], "file") && Equals(row["fullPath"], "My Folder/My Track.bin"));
    }

    [Fact]
    public async Task ReadAsync_EncryptedEntries_ReportEncryptedFailure()
    {
        await using var archive = RarTestArchiveBuilder.CreateEncrypted(("secret.txt", "hidden"));

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("RAR_ENCRYPTED", result.Error);
    }

    [Fact]
    public async Task ReadAsync_AnnouncedNextVolume_ReportsMultiVolumeFailure()
    {
        await using var archive = RarTestArchiveBuilder.CreateMultiVolume(("part.txt", "chunk"));

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("RAR_MULTIVOLUME", result.Error);
    }

    [Fact]
    public async Task ReadAsync_SplitEntryWithoutVolume_ReportsMultiVolumeFailure()
    {
        await using var archive = RarTestArchiveBuilder.CreateSplitEntry("big.bin", "first chunk");

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = archive
        });

        Assert.False(result.Success);
        Assert.StartsWith("RAR_MULTIVOLUME", result.Error);
    }

    [Fact]
    public async Task ReadAsync_CompanionVolumeNextToSource_ReportsMultiVolumeFailure()
    {
        var directory = RarTestHelpers.CreateTemporaryDirectory();
        try
        {
            var first = Path.Combine(directory, "set.part1.rar");
            await File.WriteAllBytesAsync(first, RarTestArchiveBuilder.CreateStored(("a.txt", "a")).ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, "set.part2.rar"), [1, 2, 3]);
            await using var stream = File.OpenRead(first);

            var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-rar",
                Source = stream,
                FileName = first
            });

            Assert.False(result.Success);
            Assert.StartsWith("RAR_MULTIVOLUME", result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAsync_NonSeekableSource_IsBufferedAndRead()
    {
        await using var archive = RarTestArchiveBuilder.CreateStored(("data/file.txt", "value"));
        await using var source = new RarTestHelpers.NonSeekableStream(archive.ToArray());

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = source
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "data/file.txt"));
    }
    [Fact]
    public async Task ReadAsync_CommittedFixture_ListsFolderAndNonEmptyFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Rar", "catalog.rar");
        await using var stream = File.OpenRead(path);

        var result = await new RarArchiveIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-rar",
            Source = stream,
            FileName = path
        });

        Assert.True(result.Success, result.Error);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "catalog"));
        Assert.Contains(rows, row =>
            Equals(row["fullPath"], "catalog/data/track.bin") && Equals(row["sizeBytes"], "32"));
    }
}