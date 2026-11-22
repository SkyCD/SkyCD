using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.FileFormat.Arj;
using SkyCD.Plugin.FileFormat.Arj.Tests;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.FileFormat.Arj.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ArjArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(ArjArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-arj", fileFormat.SupportedFormat.FormatId);
        Assert.Equal(".arj", Assert.Single(fileFormat.SupportedFormat.Extensions));
    }

    [Fact]
    public async Task GetWorkItemsAsync_ReturnsSafeEntries_AndSkipsUnsafePaths()
    {
        using var archive = ArjTestArchiveBuilder.CreateStored(
            ("safe.txt", "ok"),
            ("../escape.txt", "evil"),
            ("/absolute.txt", "evil"),
            ("folder/../../escape2.txt", "evil"),
            ("folder/nested.txt", "nested"));

        var set = await new ArjArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.arj"));

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
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = ArjTestArchiveBuilder.CreateDamaged();

        var set = await new ArjArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.arj"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EncryptedArchive_ReturnsEmptySet()
    {
        using var archive = ArjTestArchiveBuilder.CreateEncrypted(("secret.txt", "hidden"));

        var set = await new ArjArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "locked.arj"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var archive = ArjTestArchiveBuilder.CreateStored(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var plugin = new ArjArchiveIndexPlugin(new ArjIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "sample.arj"));

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
    public async Task GetWorkItemsAsync_NonArjSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new ArjArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var archive = ArjTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArjArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.arj"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var archive = ArjTestArchiveBuilder.CreateStored(("images/photo.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new ArjArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.arj"));

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
        using var archive = ArjTestArchiveBuilder.CreateStored(("a.txt", "a"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new ArjArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.arj"));

        Assert.Empty(indexed);
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
