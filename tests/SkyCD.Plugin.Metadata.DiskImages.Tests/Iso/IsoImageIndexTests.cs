using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Indexing;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.DiskImages;
using Xunit;
using SkyCD.Plugin.Metadata.DiskImages.Iso.Models;
using SkyCD.Plugin.Metadata.DiskImages.Models;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

public sealed class IsoImageIndexTests
{
    [Fact]
    public async Task GetWorkItemsAsync_ReturnsFiles_AndExtractionDeletesTempFile()
    {
        using var fixture = File.OpenRead(IsoImageIndexPluginTests.FixturePath("catalog-sample.iso"));
        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(fixture, "catalog-sample.iso"));
        Assert.Equal(["ROOT/DEEP/FIXTURE.TXT", "ROOT/README.MD"], set.Items.Select(item => item.Name).Order());

        var item = set.Items.Single(entry => entry.Name == "ROOT/DEEP/FIXTURE.TXT");
        Assert.Equal(19, item.SizeBytes);

        var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
        var tempPath = extracted.Name;
        var content = new byte[19];
        await using (extracted)
        {
            Assert.Equal(19, extracted.Length);
            await extracted.ReadExactlyAsync(content);
            Assert.NotEmpty(content);
        }

        Assert.StartsWith(Path.GetTempPath(), tempPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(tempPath));
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped()
    {
        using var fixture = File.OpenRead(IsoImageIndexPluginTests.FixturePath("catalog-sample.iso"));
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntryBytes: 10));

        var set = await plugin.GetWorkItemsAsync(RequestFor(fixture, "catalog-sample.iso"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("ROOT/README.MD", item.Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_MaxEntries_CapsContributedItems()
    {
        using var fixture = File.OpenRead(IsoImageIndexPluginTests.FixturePath("catalog-sample.iso"));
        var plugin = new DiskImagesIndexPlugin(new DiskImageIndexLimits(MaxEntries: 1));

        var set = await plugin.GetWorkItemsAsync(RequestFor(fixture, "catalog-sample.iso"));

        try
        {
            Assert.Single(set.Items);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonIsoFileName_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonImageSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(new byte[64 * 1024]);

        var set = await new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.iso"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var fixture = File.OpenRead(IsoImageIndexPluginTests.FixturePath("catalog-sample.iso"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DiskImagesIndexPlugin().GetWorkItemsAsync(RequestFor(fixture, "catalog-sample.iso"),
                cancellation.Token));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var fixture = File.OpenRead(IsoImageIndexPluginTests.FixturePath("catalog-sample.iso"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new DiskImagesIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(fixture, "catalog-sample.iso"));

        Assert.Equal(2, indexed.Count);
        var entry = Assert.Single(indexed, result => result.Name == "ROOT/DEEP/FIXTURE.TXT");
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.NotNull(providerResult.Metadata);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    [Fact]
    public async Task Sequence_WithoutMetadataProviders_ContributesNothing()
    {
        using var fixture = File.OpenRead(IsoImageIndexPluginTests.FixturePath("catalog-sample.iso"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new DiskImagesIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(fixture, "catalog-sample.iso"));

        Assert.Empty(indexed);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("folder/../escape.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("")]
    [InlineData("   ")]
    public void EntryPath_RejectsUnsafePaths(string path)
    {
        Assert.False(DiskImageEntryPath.IsSafeRelativePath(path));
    }

    [Theory]
    [InlineData("ROOT/DEEP/FIXTURE.TXT")]
    [InlineData("a")]
    [InlineData("folder/nested.txt")]
    public void EntryPath_AcceptsSafePaths(string path)
    {
        Assert.True(DiskImageEntryPath.IsSafeRelativePath(path));
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.Length,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

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
