using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
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
using SkyCD.Plugin.Tar;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public sealed class TarArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(TarArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-tar", fileFormat.SupportedFormat.FormatId);
    }

    [Theory]
    [InlineData(false, "sample.tar")]
    [InlineData(true, "sample.tar.gz")]
    public async Task GetWorkItemsAsync_ExtractsSafeEntries_AndSkipsUnsafePaths(bool gzip, string fileName)
    {
        using var tar = CreateTar(gzip,
            new Entry("safe.txt", "ok"),
            new Entry("../escape.txt", "evil"),
            new Entry("/absolute.txt", "evil"),
            new Entry("folder/../../escape2.txt", "evil"));

        var set = await new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(tar, fileName));

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
    public async Task GetWorkItemsAsync_NestedEntries_KeepPathsAndMetadata()
    {
        using var tar = CreateTar(false, new Entry("root/deep/notes.txt", "hello", Modified));

        var set = await new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(tar, "sample.tar"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("root/deep/notes.txt", item.Name);
            Assert.Equal(5, item.SizeBytes);
            Assert.Equal(Modified, item.ModifiedUtc);
            Assert.Equal("hello", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_SymbolicLinkEntries_AreSkipped()
    {
        using var tar = CreateTar(false,
            new Entry("real.txt", "payload"),
            new Entry("link.txt", Content: null, Type: TarEntryType.SymbolicLink, LinkName: "../outside.txt"));

        var set = await new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(tar, "sample.tar"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("real.txt", item.Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a tar archive"));

        var set = await new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.tar"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var tar = CreateTar(false, new Entry("small.txt", "ok"), new Entry("large.txt", new string('x', 64)));
        var plugin = new TarArchiveIndexPlugin(new TarIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(tar, "sample.tar"));

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
    public async Task GetWorkItemsAsync_NonTarSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var tar = CreateTar(false, new Entry("a.txt", "a"), new Entry("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(tar, "sample.tar"), cancellation.Token));
    }

    [Fact]
    public async Task GetWorkItemsAsync_CleansUpTemporaryDirectory_OnDispose()
    {
        var before = IndexDirectories();
        using var tar = CreateTar(false, new Entry("notes.txt", "hello"));

        var set = await new TarArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(tar, "sample.tar"));
        var indexDirectory = Assert.Single(IndexDirectories().Except(before));
        Assert.True(Directory.Exists(indexDirectory));

        await set.DisposeAsync();

        Assert.False(Directory.Exists(indexDirectory));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var tar = CreateTar(false, new Entry("folder/notes.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new TarArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(tar, "sample.tar"));

        var entry = Assert.Single(indexed);
        Assert.Equal("folder/notes.txt", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.NotNull(providerResult.Metadata);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    private static readonly DateTimeOffset Modified =
        new(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private static string[] IndexDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "skycd-tar-index-*");

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    private static async Task<string> ReadItemAsync(IndexWorkItem item)
    {
        await using var stream = await item.OpenSource(CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static MemoryStream CreateTar(bool gzip, params Entry[] entries)
    {
        var output = new MemoryStream();
        if (gzip)
        {
            using var gzipStream = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true);
            using var writer = new TarWriter(gzipStream, TarEntryFormat.Pax, leaveOpen: true);
            WriteEntries(writer, entries);
        }
        else
        {
            using var writer = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true);
            WriteEntries(writer, entries);
        }

        output.Position = 0;
        return output;
    }

    private static void WriteEntries(TarWriter writer, Entry[] entries)
    {
        foreach (var entry in entries)
        {
            var tarEntry = new PaxTarEntry(entry.Type, entry.Path)
            {
                ModificationTime = entry.Modified ?? Modified
            };

            if (entry.Type == TarEntryType.SymbolicLink)
            {
                tarEntry.LinkName = entry.LinkName ?? string.Empty;
            }
            else if (entry.Content is not null)
            {
                tarEntry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(entry.Content));
            }

            writer.WriteEntry(tarEntry);
        }
    }

    private sealed record Entry(
        string Path,
        string? Content,
        DateTimeOffset? Modified = null,
        TarEntryType Type = TarEntryType.RegularFile,
        string? LinkName = null);

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
