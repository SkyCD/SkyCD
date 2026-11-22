using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.FileFormat.Bzip2;
using SkyCD.Plugin.FileFormat.Bzip2.Tests;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.FileFormat.Bzip2.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class Bzip2ArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(Bzip2ArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-bzip2", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsMember_AndCleansUpOnDispose()
    {
        using var archive = Bzip2TestArchiveBuilder.Create("ok");

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.bz2"));

        var item = Assert.Single(set.Items);
        Assert.Equal("sample", item.Name);
        Assert.Equal(2, item.SizeBytes);

        var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        var tempRoot = Path.GetDirectoryName(fullPath)!;
        await using (extracted)
        {
            Assert.Equal("ok", await File.ReadAllTextAsync(fullPath));
            Assert.StartsWith(Path.GetTempPath(), tempRoot);
        }

        await set.DisposeAsync();
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public async Task GetWorkItemsAsync_CommittedFixture_ExtractsExpectedContent()
    {
        using var archive = Bzip2TestArchiveBuilder.CreateCommittedFixture();

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "catalog.bz2"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal(Bzip2TestArchiveBuilder.FixtureContent.Length, item.SizeBytes);
            await using var stream = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.Equal(Bzip2TestArchiveBuilder.FixtureContent, await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSeekableSource_ExtractsMember()
    {
        using var archive = Bzip2TestArchiveBuilder.Create("ok");
        await using var nonSeekable = new NonSeekableStream(archive);

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(nonSeekable, "sample.bz2"));

        try
        {
            var item = Assert.Single(set.Items);
            await using var stream = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.Equal("ok", await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = Bzip2TestArchiveBuilder.CreateDamaged();

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.bz2"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_InvalidLevel_ReturnsEmptySet()
    {
        using var archive = Bzip2TestArchiveBuilder.CreateInvalidLevel();

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.bz2"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_TruncatedArchive_ReturnsEmptySet()
    {
        using var truncated = Bzip2TestArchiveBuilder.CreateTruncated();

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(truncated, "broken.bz2"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_CorruptedPayload_ReturnsEmptySet()
    {
        using var corrupted = Bzip2TestArchiveBuilder.CreateCorruptedPayload();

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(corrupted, "broken.bz2"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped()
    {
        using var archive = Bzip2TestArchiveBuilder.Create(new string('x', 64));
        var plugin = new Bzip2ArchiveIndexPlugin(new Bzip2IndexLimits(MaxEntryBytes: 8));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "large.bz2"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonBzip2Source_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var archive = Bzip2TestArchiveBuilder.Create("abc");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new Bzip2ArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.bz2"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItem()
    {
        using var archive = Bzip2TestArchiveBuilder.Create("metadata-source");
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new Bzip2ArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "photo.txt.bz2"));

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
        using var archive = Bzip2TestArchiveBuilder.Create("abc");
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new Bzip2ArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.bz2"));

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
