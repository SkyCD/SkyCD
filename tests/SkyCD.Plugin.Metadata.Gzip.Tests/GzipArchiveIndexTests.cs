using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Gzip;
using SkyCD.Plugin.Metadata.Gzip.Tests;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Gzip.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class GzipArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersWorkItemProvider()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(GzipArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());

        Assert.IsType<GzipArchiveIndexPlugin>(workItems);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsNamedMember_AndCleansUpOnDispose()
    {
        using var archive = GzipTestArchiveBuilder.Create("ok", "folder/nested.txt",
            GzipTestArchiveBuilder.ModifiedUtc);

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.gz"));

        var item = Assert.Single(set.Items);
        Assert.Equal("folder/nested.txt", item.Name);
        Assert.Equal(2, item.SizeBytes);
        Assert.Equal(new DateTimeOffset(GzipTestArchiveBuilder.ModifiedUtc), item.ModifiedUtc);

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
    public async Task GetWorkItemsAsync_CompressedContent_ExtractsExpectedContent()
    {
        var content = new string('x', 8192);
        using var archive = GzipTestArchiveBuilder.Create(content, "packed.txt");

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.gz"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("packed.txt", item.Name);
            Assert.Equal(content.Length, item.SizeBytes);

            await using var stream = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.Equal(content, await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSeekableSource_ExtractsMember()
    {
        using var archive = GzipTestArchiveBuilder.Create("ok", "readme.txt");
        await using var nonSeekable = new NonSeekableStream(archive);

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(nonSeekable, "sample.gz"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("readme.txt", item.Name);
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
    public async Task GetWorkItemsAsync_UnsafeHeaderNames_NeverEscapeTemporaryRoot()
    {
        var cases = new (string HeaderName, string Expected)[]
        {
            ("/absolute.txt", "absolute.txt"),
            ("C:/drive.txt", "sample"),
            ("..", "sample"),
            ("folder/../../escape.txt", "sample")
        };

        foreach (var (headerName, expected) in cases)
        {
            using var archive = GzipTestArchiveBuilder.Create("evil", headerName);

            var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.gz"));

            try
            {
                var item = Assert.Single(set.Items);
                Assert.Equal(expected, item.Name);
                Assert.DoesNotContain("..", item.Name, StringComparison.Ordinal);

                var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
                var fullPath = extracted.Name;
                await using (extracted)
                {
                    Assert.Equal("evil", await File.ReadAllTextAsync(fullPath));
                    Assert.StartsWith(Path.GetTempPath(), fullPath);
                }
            }
            finally
            {
                await set.DisposeAsync();
            }
        }

        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "absolute.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "drive.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = GzipTestArchiveBuilder.CreateDamaged();

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.gz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_TruncatedArchive_ReturnsEmptySet()
    {
        using var truncated = GzipTestArchiveBuilder.CreateTruncated("abc");

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(truncated, "broken.gz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedMethod_ReturnsEmptySet()
    {
        using var archive = GzipTestArchiveBuilder.CreateUnsupportedMethod("abc");

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.gz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped()
    {
        using var archive = GzipTestArchiveBuilder.Create(new string('x', 64), "large.txt");
        var plugin = new GzipArchiveIndexPlugin(new GzipIndexLimits(MaxEntryBytes: 8));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "sample.gz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_CompressedTarball_ContributesNothing()
    {
        using var archive = GzipTestArchiveBuilder.Create("abc", "catalog.tar");

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "catalog.tar.gz"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonGzipSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var archive = GzipTestArchiveBuilder.Create("abc", "readme.txt");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new GzipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.gz"), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItem()
    {
        using var archive = GzipTestArchiveBuilder.Create("metadata-source", "images/photo.txt");
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new GzipArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.gz"));

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
        using var archive = GzipTestArchiveBuilder.Create("abc", "readme.txt");
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new GzipArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.gz"));

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
