using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.Split.Export;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.Feature.Split.Parsing;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public class LegacyScdCatalogSplitterTests
{
    [Fact]
    public async Task Split_NamesPartsAndPreservesEveryEntryInOrder()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.CreateDirectory("parts");

        var result = await LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, maxEntries: 5), progress: null, CancellationToken.None);

        Assert.Equal(13, catalog.EntryCount);
        Assert.Equal(["part #1.scd", "part #2.scd", "part #3.scd"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([5, 5, 3], result.Parts.Select(part => part.EntryCount));
        Assert.Equal(13, result.TotalEntries);
        Assert.Equal(catalog.Entries.Select(Describe), (await ReadBackAsync(result.Parts)).Select(Describe));
    }

    [Fact]
    public async Task Split_HonoursTheSizeLimitAgainstTheWrittenFiles()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.CreateDirectory("parts");

        var result = await LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, maxBytes: 120), progress: null, CancellationToken.None);

        Assert.NotEmpty(result.Parts);
        Assert.All(result.Parts, part =>
        {
            Assert.True(new FileInfo(part.Path).Length <= 120, $"{part.Path} is over the size limit.");
            Assert.Equal(new FileInfo(part.Path).Length, part.Bytes);
        });
        Assert.Equal(catalog.Entries.Select(Describe), (await ReadBackAsync(result.Parts)).Select(Describe));
    }

    [Fact]
    public async Task Split_LeavesTheSourceCatalogByteIdentical()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var before = File.ReadAllBytes(source);
        var written = File.GetLastWriteTimeUtc(source);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);

        await LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, workspace.CreateDirectory("parts"), maxEntries: 4), progress: null,
            CancellationToken.None);

        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(written, File.GetLastWriteTimeUtc(source));
    }

    [Fact]
    public async Task Split_WritesOnlyInsideTheChosenOutputFolder()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.CreateDirectory("parts");

        await Assert.ThrowsAsync<SplitPatternException>(() => LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, pattern: @"..\escape #N.scd", maxEntries: 4), progress: null,
            CancellationToken.None));

        Assert.Empty(Directory.GetFiles(output));
        Assert.False(File.Exists(workspace.GetPath("escape #1.scd")));
    }

    [Fact]
    public async Task Split_RefusesToReplaceAnExistingPartAndKeepsEarlierParts()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.CreateDirectory("parts");
        var occupied = workspace.CreateFile(Path.Combine("parts", "part #1.scd"), "already here");

        await Assert.ThrowsAsync<SplitOutputConflictException>(() => LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, maxEntries: 11), progress: null, CancellationToken.None));

        Assert.Equal("already here", File.ReadAllText(occupied));
        Assert.Equal(["part #1.scd"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_CancellingLeavesNoPartBehind()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.CreateDirectory("parts");
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() => LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, maxEntries: 4), new CancelAfterFirstPart(cancellation),
            cancellation.Token));

        Assert.Empty(Directory.GetFileSystemEntries(output));
        Assert.Equal(13, (await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None)).EntryCount);
    }

    [Fact]
    public async Task Split_FailureRollsBackThePartsAlreadyWritten()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.CreateDirectory("parts");
        var collision = workspace.CreateFile(Path.Combine("parts", "part #2.scd"), "taken");

        await Assert.ThrowsAsync<SplitOutputConflictException>(() => LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, maxEntries: 4), progress: null, CancellationToken.None));

        Assert.Equal("taken", File.ReadAllText(collision));
        Assert.Equal(["part #2.scd"], Directory.GetFiles(output).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Split_CreatesAMissingOutputFolderAndReportsProgress()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);
        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);
        var output = workspace.GetPath(Path.Combine("deep", "parts", "dir"));
        var progress = new List<SplitProgress>();

        var result = await LegacyScdCatalogSplitter.SplitAsync(catalog,
            Request(source, output, pattern: "{source}-{n}-of-{total}.scd", maxEntries: 8),
            new CollectProgress(progress), CancellationToken.None);

        Assert.True(Directory.Exists(output));
        Assert.Equal(["gamez-1-of-2.scd", "gamez-2-of-2.scd"],
            result.Parts.Select(part => Path.GetFileName(part.Path)));
        Assert.Equal([1, 2], progress.Select(value => value.PartsWritten));
        Assert.Equal(50, progress[0].Percent);
        Assert.Equal(100, progress[^1].Percent);
        Assert.Equal(13, progress[^1].EntriesWritten);
    }

    [Fact]
    public async Task ReadRejectsTheLegacyBinaryLayout()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateFile("binary.scd", "# SkyCD File\r\n\x1a\x04junk");

        await Assert.ThrowsAsync<NotLegacyTextCatalogException>(() =>
            LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None));
    }

    [Fact]
    public async Task ReadRejectsAFileWithoutCatalogEntries()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.CreateFile("empty.scd", "# only a comment\r\n\r\n");

        await Assert.ThrowsAsync<NotLegacyTextCatalogException>(() =>
            LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None));
    }

    [Fact]
    public async Task ReadRejectsAMissingCatalog()
    {
        using var workspace = new TestWorkspace();

        await Assert.ThrowsAsync<NotLegacyTextCatalogException>(() =>
            LegacyTextCatalogReader.ReadAsync(workspace.GetPath("absent.scd"), CancellationToken.None));
    }

    [Fact]
    public async Task ReadMeasuresTheSerializedLengthOfEveryEntryLine()
    {
        using var workspace = new TestWorkspace();
        var source = StageCatalog(workspace);

        var catalog = await LegacyTextCatalogReader.ReadAsync(source, CancellationToken.None);

        Assert.Equal(catalog.EntryCount, catalog.LineBytes.Count);
        Assert.All(catalog.LineBytes, length => Assert.True(length > 3));
        Assert.Equal(catalog.TotalBytes, catalog.LineBytes.Sum(length => (long)length));
    }

    private static string StageCatalog(TestWorkspace workspace) => workspace.CreateFile("source/gamez.scd",
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Legacy", "catalog-sample.scd")));

    private static SplitRequest Request(string source, string output, string pattern = "part #N.scd",
        int maxEntries = 0, long maxBytes = 0) => new()
        {
            SourcePath = source,
            OutputFolder = output,
            FileNamePattern = pattern,
            MaxEntriesPerPart = maxEntries,
            MaxPartSizeBytes = maxBytes
        };

    private static async Task<IReadOnlyList<LegacyScdEntry>> ReadBackAsync(IReadOnlyList<SplitPart> parts)
    {
        var entries = new List<LegacyScdEntry>();
        foreach (var part in parts)
        {
            var reopened = await LegacyTextCatalogReader.ReadAsync(part.Path, CancellationToken.None);
            entries.AddRange(reopened.Entries);
        }

        return entries;
    }

    private static string Describe(LegacyScdEntry entry) =>
        $"{entry.Path}|{entry.SizeBytes?.ToString() ?? "-"}|{entry.IsDirectory}";

    private sealed class CancelAfterFirstPart(CancellationTokenSource cancellation) : IProgress<SplitProgress>
    {
        public void Report(SplitProgress value)
        {
            if (value.PartsWritten == 1)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class CollectProgress(List<SplitProgress> sink) : IProgress<SplitProgress>
    {
        public void Report(SplitProgress value) => sink.Add(value);
    }
}
