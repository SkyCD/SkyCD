using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using Xunit;
using SkyCD.Plugin.Metadata.DiskImages;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class NrgImageIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_ListsStoredFiles_AndDeletesTemporaryFileOnDispose()
    {
        using var image = CreateImage();

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(image, "sample.nrg"));

        Assert.Equal(new[] { "ROOT/DEEP/FIXTURE.TXT", "ROOT/README.TXT" }, set.Items.Select(item => item.Name));
        var item = Assert.Single(set.Items, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        Assert.Equal(19, item.SizeBytes);
        Assert.NotNull(item.ModifiedUtc);

        var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
        var tempPath = extracted.Name;
        Assert.True(File.Exists(tempPath));
        await using (extracted)
        {
            using var reader = new StreamReader(extracted, Encoding.UTF8, leaveOpen: true);
            Assert.Equal("nrg fixture content", await reader.ReadToEndAsync());
        }

        Assert.False(File.Exists(tempPath));
        await set.DisposeAsync();
    }

    [Theory]
    [InlineData(2336, 0x0300)]
    [InlineData(2352, 0x0500)]
    [InlineData(2352, 0x0600)]
    public async Task GetWorkItemsAsync_ExtractsFilesFromRawSectorTracks(int sectorSize, ushort mode)
    {
        using var image = CreateImage(sectorSize, mode);

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(image, "sample.nrg"));

        var item = Assert.Single(set.Items, candidate => candidate.Name == "ROOT/README.TXT");
        await using var extracted = await item.OpenSource(CancellationToken.None);
        Assert.Equal("readme", await ReadAllAsync(extracted));
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_ResolvesDataTrackAfterTrackPrefix()
    {
        using var image = CreateImage(trackPrefix: new byte[4096]);

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(image, "sample.nrg"));

        Assert.Equal(2, set.Items.Count);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_VersionOneImage_IsSupported()
    {
        using var image = CreateImage(version: 1);

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(image, "sample.nrg"));

        var item = Assert.Single(set.Items, candidate => candidate.Name == "ROOT/README.TXT");
        await using var extracted = await item.OpenSource(CancellationToken.None);
        Assert.Equal("readme", await ReadAllAsync(extracted));
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedImage_ReturnsEmptySet()
    {
        using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not an nrg image"));

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.nrg"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_AudioOnlyImage_ReturnsEmptySet()
    {
        using var image = CreateImage(sectorSize: 2352, mode: 0x0700);

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(image, "audio.nrg"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonNrgSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var image = CreateImage();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(image, "sample.nrg"), cancellation.Token));
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var image = CreateImage();
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntries: 100, MaxEntryBytes: 8));

        var set = await plugin.GetWorkItemsAsync(RequestFor(image, "sample.nrg"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("ROOT/README.TXT", item.Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_MaxEntries_LimitsContributedItems()
    {
        using var image = CreateImage();
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntries: 1));

        var set = await plugin.GetWorkItemsAsync(RequestFor(image, "sample.nrg"));

        try
        {
            Assert.Single(set.Items);
        }
        finally
        {
            await set.DisposeAsync();
        }
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
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var image = CreateImage();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new DiskImagesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(image, "sample.nrg"));

        Assert.Equal(2, indexed.Count);
        var entry = Assert.Single(indexed, candidate => candidate.Name == "ROOT/DEEP/FIXTURE.TXT");
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        using var image = CreateImage();
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new DiskImagesIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(image, "sample.nrg"));

        Assert.Empty(indexed);
    }

    private static MemoryStream CreateImage(int sectorSize = 2048, ushort mode = 0, int version = 2,
        byte[]? trackPrefix = null) =>
        new(NrgImageBuilder.Create(NrgImageBuilder.BuildIso(), sectorSize, mode, version, trackPrefix));

    private static async Task<string> ReadAllAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
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
