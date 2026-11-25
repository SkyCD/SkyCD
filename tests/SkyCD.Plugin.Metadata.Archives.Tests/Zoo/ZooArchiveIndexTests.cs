using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Metadata.Archives;
using SkyCD.Plugin.Metadata.Archives.Zoo;
using SkyCD.Plugin.Metadata.Archives.Zoo.Tests;
using Xunit;
using SkyCD.Plugin.Metadata.Archives.Zoo.Models;
using SkyCD.Plugin.Metadata.Archives.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ZooArchiveIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_ReturnsSafeEntries_AndSkipsUnsafePaths()
    {
        using var archive = ZooTestArchiveBuilder.CreateStored(
            ("safe.txt", "ok"),
            ("../escape.txt", "evil"),
            ("/absolute.txt", "evil"),
            ("folder/../../escape2.txt", "evil"),
            ("folder/nested.txt", "nested"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.zoo"));

        Assert.Equal(["folder/nested.txt", "safe.txt"], set.Items.Select(item => item.Name).Order());
        var safe = set.Items.Single(item => item.Name == "safe.txt");
        Assert.Equal(2, safe.SizeBytes);

        var extracted = Assert.IsType<FileStream>(await safe.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        var tempRoot = Path.GetDirectoryName(fullPath)!;
        await using (extracted)
        {
            Assert.Equal("ok", await File.ReadAllTextAsync(fullPath));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(tempRoot, "escape*.txt", SearchOption.AllDirectories),
                path => path.Length > 0);
        }

        await set.DisposeAsync();
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(tempRoot));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_LzdMembers_RoundTripContent()
    {
        var binary = new byte[70_000];
        new Random(716).NextBytes(binary);
        var text = string.Concat(Enumerable.Repeat("zoo archive metadata payload; ", 512));
        using var archive = ZooTestArchiveBuilder.CreateLzd(
            ("data/track.bin", binary),
            ("notes.txt", Encoding.UTF8.GetBytes(text)));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.zoo"));

        try
        {
            Assert.Equal(["data/track.bin", "notes.txt"], set.Items.Select(item => item.Name).Order());
            await AssertExtractedAsync(set, "data/track.bin", binary);
            await AssertExtractedAsync(set, "notes.txt", Encoding.UTF8.GetBytes(text));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_CorruptCrc_SkipsOnlyThatEntry()
    {
        using var archive = ZooTestArchiveBuilder.CreateWithCorruptCrc(("bad.txt", "bad"), ("good.txt", "good"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.zoo"));

        try
        {
            Assert.Equal(["good.txt"], set.Items.Select(item => item.Name));
            await AssertExtractedAsync(set, "good.txt", Encoding.UTF8.GetBytes("good"));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedPackingMethod_IsSkipped()
    {
        using var archive = ZooTestArchiveBuilder.CreateUnsupported(("lzh.txt", "payload"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.zoo"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = ZooTestArchiveBuilder.CreateDamaged();

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.zoo"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var archive = ZooTestArchiveBuilder.CreateStored(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var plugin = new ArchivesIndexPlugin(new ArchiveIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "sample.zoo"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("small.txt", item.Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonZooSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var archive = ZooTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.zoo"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var archive = ZooTestArchiveBuilder.CreateStored(("images/photo.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new ArchivesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.zoo"));

        var entry = Assert.Single(indexed);
        Assert.Equal("images/photo.txt", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.NotNull(providerResult.Metadata);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        using var archive = ZooTestArchiveBuilder.CreateStored(("a.txt", "a"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new ArchivesIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.zoo"));

        Assert.Empty(indexed);
    }

    private static async Task AssertExtractedAsync(IndexWorkItemSet set, string name, byte[] expected)
    {
        var item = set.Items.Single(candidate => candidate.Name == name);
        await using var extracted = await item.OpenSource(CancellationToken.None);
        using var buffer = new MemoryStream();
        await extracted.CopyToAsync(buffer);
        Assert.Equal(expected, buffer.ToArray());
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    /// <summary>Lets the plugin dispose its source stream while the test keeps ownership of the memory stream.</summary>
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
