using System;
using System.Collections.Generic;
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
using SkyCD.Plugin.FileFormat.SevenZip;
using Xunit;
using SkyCD.Plugin.FileFormat.SevenZip.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class SevenZipIndexWorkItemTests
{
    private static string SampleArchivePath =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "7z", "sample.7z");

    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(SevenZipArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-7z", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsArchiveFiles_AndCleansUpOnDispose()
    {
        await using var source = File.OpenRead(SampleArchivePath);
        var request = RequestFor(source, "sample.7z");

        var set = await new SevenZipArchiveIndexPlugin().GetWorkItemsAsync(request);

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("root/deep/įrašas.txt", item.Name);
            Assert.True(item.SizeBytes > 0);

            var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
            var fullPath = extracted.Name;
            await using (extracted)
            {
                Assert.True(await File.ReadAllTextAsync(fullPath) is { Length: > 0 });
            }

            await set.DisposeAsync();
            Assert.False(File.Exists(fullPath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(fullPath)));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped()
    {
        await using var source = File.OpenRead(SampleArchivePath);
        var plugin = new SevenZipArchiveIndexPlugin(
            new SevenZipIndexLimits(MaxEntries: 100, MaxEntryBytes: 1, MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(source, "sample.7z"));

        try
        {
            Assert.Empty(set.Items);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a 7z archive"));

        var set = await new SevenZipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.7z"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSevenZipSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new SevenZipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        await using var source = File.OpenRead(SampleArchivePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SevenZipArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(source, "sample.7z"),
                cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        await using var source = File.OpenRead(SampleArchivePath);
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new SevenZipArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(source, "sample.7z"));

        var entry = Assert.Single(indexed);
        Assert.Equal("root/deep/įrašas.txt", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        await using var source = File.OpenRead(SampleArchivePath);
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new SevenZipArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(source, "sample.7z"));

        Assert.Empty(indexed);
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
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
