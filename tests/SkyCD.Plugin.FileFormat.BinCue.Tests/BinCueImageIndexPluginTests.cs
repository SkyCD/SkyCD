using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.FileFormat.BinCue.Exceptions;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.FileFormat.BinCue.Models;

namespace SkyCD.Plugin.FileFormat.BinCue.Tests;

public sealed class BinCueImageIndexPluginTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(BinCueImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-bincue", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public void OpenFormats_IncludeCue_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new BinCueImageIndexPlugin()]);

        Assert.Contains(service.GetOpenFormats(),
            format => format.FormatId == "skycd-bincue" && format.Extensions.Contains(".cue"));
        Assert.DoesNotContain(service.GetSaveFormats(), format => format.FormatId == "skycd-bincue");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyCueFormat()
    {
        var service = new FileFormatManager([new BinCueImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-bincue",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2048, 0, "MODE1/2048")]
    [InlineData(2352, 16, "MODE1/2352")]
    [InlineData(2352, 24, "MODE2/2352")]
    [InlineData(2336, 8, "MODE2/2336")]
    public async Task ReadAsync_SectorLayout_ExposesIsoFilesystem(int sectorSize, int dataOffset, string mode)
    {
        using var temp = new TempDirectory();
        var image = BinCueTestImage.Repack(BinCueTestImage.BuildIso(), sectorSize, dataOffset);
        var sheet = BinCueTestImage.BuildSheet([(mode, 0)]);
        var path = BinCueTestImage.WriteSet(temp.Path, "catalog", image, sheet);

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        var rows = Rows(result);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP") && Equals(row["kind"], "folder"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("FIXTURE.TXT", file["name"]);
        Assert.Equal(BinCueTestImage.FixtureContent.Length.ToString(), file["sizeBytes"]);
    }

    [Fact]
    public async Task ReadAsync_DataTrackAfterAudioTrack_IsRead()
    {
        using var temp = new TempDirectory();
        var image = BinCueTestImage.WithAudioPrefix(BinCueTestImage.BuildIso(), audioSectors: 10);
        var sheet = BinCueTestImage.BuildSheet([("AUDIO", 0), ("MODE1/2352", 10)], fileName: "mixed.bin");
        var path = BinCueTestImage.WriteSet(temp.Path, "mixed", image, sheet);

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    [Fact]
    public async Task ReadAsync_DataTrackEndingAtNextTrack_StopsAtTrackBoundary()
    {
        using var temp = new TempDirectory();
        var iso = BinCueTestImage.BuildIso();
        var data = BinCueTestImage.Repack(iso, 2352, 16);
        var image = new byte[data.Length + 10 * 2352];
        Buffer.BlockCopy(data, 0, image, 0, data.Length);
        var dataSectors = iso.Length / 2048;
        var sheet = BinCueTestImage.BuildSheet([("MODE1/2352", 0), ("AUDIO", dataSectors)],
            fileName: "boundary.bin");
        var path = BinCueTestImage.WriteSet(temp.Path, "boundary", image, sheet);

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/README.TXT"));
    }

    [Fact]
    public async Task ReadAsync_FileEntryWithSpaces_IsResolvedCaseInsensitively()
    {
        using var temp = new TempDirectory();
        var image = BinCueTestImage.Repack(BinCueTestImage.BuildIso(), 2352, 16);
        var sheet = BinCueTestImage.BuildSheet([("MODE1/2352", 0)], fileName: "My Disc.BIN");
        var path = BinCueTestImage.WriteSet(temp.Path, "spaced", image, sheet, imageFileName: "my disc.bin");

        var result = await ReadAsync(path);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    [Fact]
    public async Task ReadAsync_MissingCompanion_FailsWithCompanionName()
    {
        using var temp = new TempDirectory();
        var sheetPath = Path.Combine(temp.Path, "catalog.cue");
        await File.WriteAllTextAsync(sheetPath, BinCueTestImage.BuildSheet([("MODE1/2352", 0)]));

        var result = await ReadAsync(sheetPath);

        Assert.False(result.Success);
        Assert.Contains("catalog.bin", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_SheetTraversal_DoesNotResolveOutsideDirectory()
    {
        using var temp = new TempDirectory();
        var setDirectory = Path.Combine(temp.Path, "set");
        File.WriteAllBytes(Path.Combine(temp.Path, "escape.bin"), BinCueTestImage.BuildIso());
        var sheet = BinCueTestImage.BuildSheet([("MODE1/2048", 0)], fileName: "../escape.bin");
        var path = BinCueTestImage.WriteSet(setDirectory, "catalog", [], sheet);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("escape.bin", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_BinarySheet_FailsGracefully()
    {
        using var temp = new TempDirectory();
        var sheet = new string('x', 32) + "\0FILE";
        var path = BinCueTestImage.WriteSet(temp.Path, "catalog", BinCueTestImage.BuildIso(), sheet);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("CUE sheet", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_AudioOnlySheet_FailsWithNoDataTrack()
    {
        using var temp = new TempDirectory();
        var sheet = BinCueTestImage.BuildSheet([("AUDIO", 0)]);
        var path = BinCueTestImage.WriteSet(temp.Path, "audio",
            BinCueTestImage.Repack(BinCueTestImage.BuildIso(), 2352, 16), sheet);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("data track", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_WaveFileTrack_IsRejectedAsUnsupported()
    {
        using var temp = new TempDirectory();
        var sheet = BinCueTestImage.BuildSheet([("MODE1/2048", 0)], fileType: "WAVE");
        var path = BinCueTestImage.WriteSet(temp.Path, "catalog", BinCueTestImage.BuildIso(), sheet);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_DataTrackWithoutIndex_FailsGracefully()
    {
        using var temp = new TempDirectory();
        const string sheet = "FILE \"catalog.bin\" BINARY\n  TRACK 01 MODE1/2352\n";
        var path = BinCueTestImage.WriteSet(temp.Path, "catalog", BinCueTestImage.BuildIso(), sheet);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("INDEX 01", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_DataTrackOutsideImage_FailsGracefully()
    {
        using var temp = new TempDirectory();
        const string sheet = "FILE \"catalog.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 40:00:00\n";
        var path = BinCueTestImage.WriteSet(temp.Path, "catalog", BinCueTestImage.BuildIso(), sheet);

        var result = await ReadAsync(path);

        Assert.False(result.Success);
        Assert.Contains("outside", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CueSheet_RejectsInvalidTimeStamp()
    {
        Assert.Throws<BinCueImageFormatException>(() => CueSheet.Parse(Encoding.UTF8.GetBytes(
            "FILE \"a.bin\" BINARY\nTRACK 01 MODE1/2352\nINDEX 01 00:99:00\n")));
    }

    [Fact]
    public void CueSheet_IgnoresPregapIndexZero()
    {
        var sheet = CueSheet.Parse(Encoding.UTF8.GetBytes(
            "FILE \"a.bin\" BINARY\nTRACK 01 MODE1/2352\nINDEX 00 00:00:00\nINDEX 01 00:00:02\n"));

        var track = Assert.Single(sheet.Tracks);
        Assert.Equal(2, track.Index1);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ListsContainedFiles_AndOpensTheirContent()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var before = TemporaryEntryFiles();

        await using var set = await new BinCueImageIndexPlugin().GetWorkItemsAsync(RequestFor(path));

        Assert.Equal(2, set.Items.Count(item => item.Name.EndsWith(".TXT", StringComparison.Ordinal)));
        var item = Assert.Single(set.Items, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        Assert.Equal(BinCueTestImage.FixtureContent.Length, item.SizeBytes);
        await using (var content = await item.OpenSource(CancellationToken.None))
        using (var reader = new StreamReader(content, Encoding.UTF8))
        {
            Assert.Equal(BinCueTestImage.FixtureContent, await reader.ReadToEndAsync());
        }

        Assert.Equal(before, TemporaryEntryFiles());
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonCueSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        await using var set = await new BinCueImageIndexPlugin().GetWorkItemsAsync(new IndexWorkItemRequest
        {
            FileName = "notes.txt",
            SizeBytes = stream.Length,
            OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
        });

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MissingCompanion_ReturnsEmptySet()
    {
        using var temp = new TempDirectory();
        var sheetPath = Path.Combine(temp.Path, "catalog.cue");
        await File.WriteAllTextAsync(sheetPath, BinCueTestImage.BuildSheet([("MODE1/2352", 0)]));

        await using var set = await new BinCueImageIndexPlugin().GetWorkItemsAsync(RequestFor(sheetPath));

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new BinCueImageIndexPlugin(new BinCueIndexLimits(MaxEntries: 100, MaxEntryBytes: 8));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.Equal("ROOT/README.TXT", item.Name);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MaxEntries_LimitsContributedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new BinCueImageIndexPlugin(new BinCueIndexLimits(MaxEntries: 1));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        Assert.Single(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_TotalBudget_BoundsMaterializedBytes()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new BinCueImageIndexPlugin(new BinCueIndexLimits(MaxTotalBytes: 20));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.True(item.SizeBytes <= 20);
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BinCueImageIndexPlugin().GetWorkItemsAsync(RequestFor(path), cancellation.Token));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Constructor_RejectsNonPositiveLimits(int maxEntries, long maxEntryBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BinCueImageIndexPlugin(new BinCueIndexLimits(maxEntries, maxEntryBytes)));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContainedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new BinCueImageIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(path));

        Assert.Equal(2, indexed.Count);
        var entry = Assert.Single(indexed, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Fixture_ReadsThroughCommittedFiles()
    {
        var sheetPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "BinCue", "catalog-sample.cue");
        Assert.True(File.Exists(sheetPath));
        Assert.True(File.Exists(Path.ChangeExtension(sheetPath, ".bin")));

        var result = await ReadAsync(sheetPath);

        Assert.True(result.Success, result.Error);
        Assert.Contains(Rows(result), row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    private static async Task<FileFormatReadResult> ReadAsync(string sheetPath)
    {
        await using var source = File.OpenRead(sheetPath);
        return await new BinCueImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-bincue",
            Source = source,
            FileName = Path.GetFileName(sheetPath),
            SourcePath = sheetPath
        });
    }

    private static List<Dictionary<string, object?>> Rows(FileFormatReadResult result) =>
        Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

    private static string WriteCatalog(string directory)
    {
        var image = BinCueTestImage.Repack(BinCueTestImage.BuildIso(), 2352, 16);
        var sheet = BinCueTestImage.BuildSheet([("MODE1/2352", 0)]);
        return BinCueTestImage.WriteSet(directory, "catalog", image, sheet);
    }

    private static IndexWorkItemRequest RequestFor(string sheetPath) => new()
    {
        FileName = Path.GetFileName(sheetPath),
        SizeBytes = new FileInfo(sheetPath).Length,
        SourcePath = sheetPath,
        OpenSource = _ => Task.FromResult<Stream>(File.OpenRead(sheetPath))
    };

    private static List<string> TemporaryEntryFiles() =>
        Directory.Exists(Path.GetTempPath())
            ? Directory.GetFiles(Path.GetTempPath(), "skycd-bincue-entry-*.tmp").ToList()
            : [];

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"skycd-bincue-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>Lets the plugin dispose its stream while the test keeps ownership of the memory stream.</summary>
    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class TestMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor { get; } = new(
            Extensions: ["*"],
            ContentTypes: ["*"],
            Schema: new MediaMetadataSchema("test.schema", 1,
                [new MediaMetadataField("indexed", MediaMetadataType.Text)]),
            Priority: 0,
            IsOptIn: false);

        public Task<MediaExtractionResult> ExtractAsync(
            MediaItem item,
            IMediaSource source,
            IProgress<MediaProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new MediaExtractionResult([
                new MediaMetadataValue("indexed", MediaMetadataType.Text, Text: "yes")
            ]));
        }
    }
}
