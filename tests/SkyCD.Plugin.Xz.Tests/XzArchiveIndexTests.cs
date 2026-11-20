using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Xz;
using SkyCD.Plugin.Xz.Tests;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class XzArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(XzArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-xz", fileFormat.SupportedFormat.FormatId);
        Assert.Equal(new[] { ".xz", ".txz" }, fileFormat.SupportedFormat.Extensions);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsMember_AndCleansUpOnDispose()
    {
        using var archive = XzTestArchiveBuilder.CreateValid();

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "catalog.xz"));

        var item = Assert.Single(set.Items);
        Assert.Equal("catalog", item.Name);
        Assert.Equal(XzTestArchiveBuilder.FixtureContent.Length, item.SizeBytes);

        var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        var tempRoot = Path.GetDirectoryName(fullPath)!;
        await using (extracted)
        {
            Assert.Equal(XzTestArchiveBuilder.FixtureContent, await File.ReadAllTextAsync(fullPath));
            Assert.StartsWith(Path.GetTempPath(), tempRoot);
        }

        await set.DisposeAsync();
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSeekableSource_ExtractsMember()
    {
        using var archive = XzTestArchiveBuilder.CreateValid();
        await using var nonSeekable = new NonSeekableStream(archive);

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(nonSeekable, "catalog.xz"));

        try
        {
            var item = Assert.Single(set.Items);
            await using var stream = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.Equal(XzTestArchiveBuilder.FixtureContent, await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = XzTestArchiveBuilder.CreateDamaged();

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.xz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_TruncatedArchive_ReturnsEmptySet()
    {
        using var truncated = XzTestArchiveBuilder.CreateTruncated();

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(truncated, "broken.xz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_CorruptedPayload_ReturnsEmptySet()
    {
        using var corrupted = XzTestArchiveBuilder.CreateCorruptedPayload();

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(corrupted, "broken.xz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedStreamFlags_ReturnsEmptySet()
    {
        using var archive = XzTestArchiveBuilder.CreateUnsupportedFlags();

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.xz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped()
    {
        using var archive = XzTestArchiveBuilder.CreateValid();
        var plugin = new XzArchiveIndexPlugin(new XzIndexLimits(MaxEntryBytes: 8));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "catalog.xz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonXzSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var archive = XzTestArchiveBuilder.CreateValid();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new XzArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "catalog.xz"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItem()
    {
        using var archive = XzTestArchiveBuilder.CreateValid();
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new XzArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "photo.txt.xz"));

        var entry = Assert.Single(indexed);
        Assert.Equal("photo.txt", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.NotNull(providerResult.Metadata);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        using var archive = XzTestArchiveBuilder.CreateValid();
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new XzArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(archive, "catalog.xz"));

        Assert.Empty(indexed);
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.CanSeek ? stream.Length : 0,
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
