using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Vmdk;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using Xunit;
using SkyCD.Plugin.Metadata.DiskImages.Vmdk.Models;
using SkyCD.Plugin.Metadata.DiskImages;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class VmdkDiskImageIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_ListsContainedFiles_WithoutExtractingAny()
    {
        using var fixture = VmdkImageFixture.Create(("folder/notes.txt", "hello"));
        var before = IndexDirectories();

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestForPath(fixture.ImagePath));

        var indexDirectory = Assert.Single(IndexDirectories().Except(before));
        try
        {
            var item = Assert.Single(set.Items);
            Assert.EndsWith("notes.txt", item.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(5, item.SizeBytes);
            Assert.Empty(System.IO.Directory.EnumerateFiles(indexDirectory, "*", SearchOption.AllDirectories));

            await using (var content = await item.OpenSource(CancellationToken.None))
            {
                using var reader = new StreamReader(content, Encoding.UTF8);
                Assert.Equal("hello", await reader.ReadToEndAsync());
            }

            Assert.Empty(System.IO.Directory.EnumerateFiles(indexDirectory, "*", SearchOption.AllDirectories));
        }
        finally
        {
            await set.DisposeAsync();
        }

        Assert.False(System.IO.Directory.Exists(indexDirectory));
    }

    [Fact]
    public async Task GetWorkItemsAsync_SplitImage_ResolvesCompanionExtentsFromSourcePath()
    {
        using var fixture = VmdkImageFixture.Create(DiskCreateType.MonolithicFlat, ("notes.txt", "split"));
        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestForPath(fixture.ImagePath));

        try
        {
            var item = Assert.Single(set.Items);
            await using var content = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(content, Encoding.UTF8);
            Assert.Equal("split", await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedImage_ReturnsEmptySet()
    {
        await using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a vmdk image"));

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestForStream(broken, "broken.vmdk"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DescriptorExtentOutsideImageDirectory_ReturnsEmptySet()
    {
        using var fixture = VmdkImageFixture.Create(DiskCreateType.MonolithicFlat);
        var descriptor = await File.ReadAllTextAsync(fixture.ImagePath);
        var unsafeDescriptor = descriptor.Replace("disk-flat.vmdk", "../disk-flat.vmdk",
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(descriptor, unsafeDescriptor);
        await File.WriteAllTextAsync(fixture.ImagePath, unsafeDescriptor);

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestForPath(fixture.ImagePath));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var fixture = VmdkImageFixture.Create(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestForPath(fixture.ImagePath));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.EndsWith("small.txt", item.Name, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonVmdkSource_ReturnsEmptySet()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestForStream(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var fixture = VmdkImageFixture.Create(("a.txt", "a"), ("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestForPath(fixture.ImagePath),
                cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContainedFiles()
    {
        using var fixture = VmdkImageFixture.Create(("photos/note.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new DiskImagesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestForPath(fixture.ImagePath));

        var entry = Assert.Single(indexed);
        Assert.EndsWith("note.txt", entry.Name, StringComparison.OrdinalIgnoreCase);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.NotNull(providerResult.Metadata);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        using var fixture = VmdkImageFixture.Create(("a.txt", "a"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new DiskImagesIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestForPath(fixture.ImagePath));

        Assert.Empty(indexed);
    }

    private static string[] IndexDirectories() =>
        System.IO.Directory.GetDirectories(Path.GetTempPath(), "skycd-vmdk-index-*");

    private static IndexWorkItemRequest RequestForPath(string imagePath) => new()
    {
        FileName = Path.GetFileName(imagePath),
        SizeBytes = new FileInfo(imagePath).Length,
        SourcePath = imagePath,
        OpenSource = _ => Task.FromResult<Stream>(
            new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    };

    private static IndexWorkItemRequest RequestForStream(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    /// <summary>Lets the plugin dispose its source stream while the test keeps ownership of the stream.</summary>
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
