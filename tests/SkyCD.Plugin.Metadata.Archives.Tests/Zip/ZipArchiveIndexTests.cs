using System;
using System.IO;
using System.IO.Compression;
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
using SkyCD.Plugin.Metadata.Archives.Zip;
using Xunit;
using SkyCD.Plugin.Metadata.Archives.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class ZipArchiveIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_ReturnsSafeEntries_AndSkipsUnsafePaths()
    {
        using var zip = CreateZip(
            ("safe.txt", "ok"),
            ("../escape.txt", "evil"),
            ("/absolute.txt", "evil"),
            ("folder/../../escape2.txt", "evil"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(zip, "sample.zip"));

        var item = Assert.Single(set.Items);
        Assert.Equal("safe.txt", item.Name);
        Assert.Equal(2, item.SizeBytes);

        var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        await using (extracted)
        {
            Assert.Equal("ok", await File.ReadAllTextAsync(fullPath));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(Path.GetDirectoryName(fullPath)!, "escape*.txt", SearchOption.AllDirectories),
                path => path.Length > 0);
        }

        await set.DisposeAsync();
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(fullPath)));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.zip"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var zip = CreateZip(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var plugin = new ArchivesIndexPlugin(new ArchiveIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(zip, "sample.zip"));

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
    public async Task GetWorkItemsAsync_NonZipSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var zip = CreateZip(("a.txt", "a"), ("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ArchivesIndexPlugin().GetWorkItemsAsync(RequestFor(zip, "sample.zip"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var zip = CreateZip(("images/photo.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new ArchivesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(zip, "sample.zip"));

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
        using var zip = CreateZip(("a.txt", "a"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new ArchivesIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(zip, "sample.zip"));

        Assert.Empty(indexed);
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    private static MemoryStream CreateZip(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false), leaveOpen: false);
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

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
