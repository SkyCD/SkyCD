using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.DiskImages.BinCue.Exceptions;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;
using SkyCD.Plugin.Metadata.DiskImages.BinCue.Models;
using SkyCD.Plugin.Metadata.DiskImages;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class BinCueImageIndexTests
{
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

        await using var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(path));

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

        await using var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(new IndexWorkItemRequest
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

        await using var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(sheetPath));

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntries: 100, MaxEntryBytes: 8));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        var item = Assert.Single(set.Items);
        Assert.Equal("ROOT/README.TXT", item.Name);
    }

    [Fact]
    public async Task GetWorkItemsAsync_MaxEntries_LimitsContributedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntries: 1));

        await using var set = await plugin.GetWorkItemsAsync(RequestFor(path));

        Assert.Single(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_TotalBudget_BoundsMaterializedBytes()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxTotalBytes: 20));

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
            new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(path), cancellation.Token));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Constructor_RejectsNonPositiveLimits(int maxEntries, long maxEntryBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DiskImagesIndexPlugin(new DiskImageIndexLimits(maxEntries, maxEntryBytes)));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContainedItems()
    {
        using var temp = new TempDirectory();
        var path = WriteCatalog(temp.Path);
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new DiskImagesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(path));

        Assert.Equal(2, indexed.Count);
        var entry = Assert.Single(indexed, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

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
