using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.FileFormat.Rar;
using SkyCD.Plugin.FileFormat.Rar.Tests;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.FileFormat.Rar.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class RarArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(RarArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-rar", fileFormat.SupportedFormat.FormatId);
        Assert.Equal(".rar", Assert.Single(fileFormat.SupportedFormat.Extensions));
    }

    [Fact]
    public void Constructor_RejectsInvalidLimits()
    {
        Assert.Throws<ArgumentNullException>(() => new RarArchiveIndexPlugin(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RarArchiveIndexPlugin(new RarIndexLimits(MaxEntries: 0)));
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsStoredEntries_AndSkipsUnsafePaths()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(
            ("safe.txt", "ok"),
            ("../escape.txt", "evil"),
            ("/absolute.txt", "evil"),
            ("folder/../../escape2.txt", "evil"),
            ("C:\\windows\\escape3.txt", "evil"),
            ("folder/nested.txt", "nested"));

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "sample.rar"));

        Assert.Equal(["folder/nested.txt", "safe.txt"], set.Items.Select(item => item.Name).Order());

        var safe = set.Items.Single(item => item.Name == "safe.txt");
        Assert.Equal(2, safe.SizeBytes);
        Assert.NotNull(safe.ModifiedUtc);

        var extracted = Assert.IsType<FileStream>(await safe.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        var tempRoot = Path.GetDirectoryName(fullPath)!;
        await using (extracted)
        {
            Assert.Equal("ok", await File.ReadAllTextAsync(fullPath));
            Assert.Empty(Directory.EnumerateFiles(tempRoot, "escape*.txt", SearchOption.AllDirectories));
        }

        await set.DisposeAsync();
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(tempRoot));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_DuplicateEntryNames_ExtractFirstOccurrenceOnly()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("dup.txt", "first"), ("dup.txt", "second"));

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "sample.rar"));
        try
        {
            var item = Assert.Single(set.Items);
            await using var read = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(read);
            Assert.Equal("first", await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_SkipsDirectoryLikeServiceHeaders()
    {
        using var archive = RarTestArchiveBuilder.CreateWithServiceHeaders(("folder/data.txt", "payload"));

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "sample.rar"));
        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("folder/data.txt", item.Name);
            Assert.Equal(7, item.SizeBytes);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonRarExtension_ReturnsEmptySet()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("a.txt", "a"));

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "sample.zip"));

        Assert.Empty(set.Items);
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonRarContent_ReturnsEmptySet()
    {
        using var archive = RarTestArchiveBuilder.CreateDamaged();
        var existing = Directory.GetDirectories(Path.GetTempPath(), "skycd-rar-index-*").ToHashSet();

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "broken.rar"));

        Assert.Empty(set.Items);
        var created = Directory.GetDirectories(Path.GetTempPath(), "skycd-rar-index-*")
            .Where(path => !existing.Contains(path));
        Assert.Empty(created);
    }

    [Fact]
    public async Task GetWorkItemsAsync_EncryptedArchive_ReturnsEmptySet()
    {
        using var archive = RarTestArchiveBuilder.CreateEncrypted(("secret.txt", "hidden"));

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "locked.rar"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_MultiVolumeArchive_ReturnsEmptySet()
    {
        using var archive = RarTestArchiveBuilder.CreateMultiVolume(("part.txt", "chunk"));

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(archive, "set.rar"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_CompanionVolumeNextToSource_ReturnsEmptySet()
    {
        var directory = RarTestHelpers.CreateTemporaryDirectory();
        try
        {
            using var archive = RarTestArchiveBuilder.CreateStored(("part.txt", "chunk"));
            var first = Path.Combine(directory, "set.part1.rar");
            await File.WriteAllBytesAsync(first, archive.ToArray());
            await File.WriteAllBytesAsync(Path.Combine(directory, "set.part2.rar"), [1, 2, 3]);

            var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
                RarTestHelpers.RequestFor(archive, first, first));

            Assert.Empty(set.Items);
            await set.DisposeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var data = archive.ToArray();
        using var source = new MemoryStream(data);
        var plugin = new RarArchiveIndexPlugin(new RarIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RarTestHelpers.RequestFor(source, "sample.rar", data.Length));

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
    public async Task GetWorkItemsAsync_NonSeekableSource_IsBufferedInsideTemporaryDirectory()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("data/file.txt", "value"));
        await using var source = new RarTestHelpers.NonSeekableStream(archive.ToArray());

        var set = await new RarArchiveIndexPlugin().GetWorkItemsAsync(
            RarTestHelpers.RequestFor(source, "pipe.rar", archive.Length));
        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("data/file.txt", item.Name);
            await using var read = await item.OpenSource(CancellationToken.None);
            var extractedPath = Assert.IsType<FileStream>(read).Name;
            await using (read)
            {
                Assert.Equal("value", await File.ReadAllTextAsync(extractedPath));
            }
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates_AndCleansUp()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"));
        var data = archive.ToArray();
        using var cancellation = new CancellationTokenSource();
        using var source = new RarTestHelpers.CancellingStream(new MemoryStream(data), bytesBeforeCancel: 16,
            cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RarArchiveIndexPlugin().GetWorkItemsAsync(
                RarTestHelpers.RequestFor(source, "sample.rar", data.Length), cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("images/photo.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new RarArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RarTestHelpers.RequestFor(archive, "sample.rar"));

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
        using var archive = RarTestArchiveBuilder.CreateStored(("a.txt", "a"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new RarArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RarTestHelpers.RequestFor(archive, "sample.rar"));

        Assert.Empty(indexed);
    }

    private sealed class TestMetadataCapability : IMediaMetadataCapability
    {
        public MediaDescriptor Descriptor { get; } = new(
            Extensions: ["*"],
            ContentTypes: [".txt"],
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
