using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Ace;
using SkyCD.Plugin.Metadata.Ace.Tests;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Ace.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class AceArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersWorkItemProvider()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AceArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());

        Assert.IsType<AceArchiveIndexPlugin>(workItems);
    }

    [Fact]
    public void AssemblyMetadata_DeclaresAcePluginId()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AceArchiveIndexPlugin).Assembly);

        Assert.Equal("skycd.ace", plugin.Id);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsStoredEntries_WithVerifiedCrc_AndCleansUp()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(("docs/readme.txt", "hello ace"));
        var plugin = new AceArchiveIndexPlugin();

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        Assert.Empty(plugin.LastDiagnostics);
        var item = Assert.Single(set.Items);
        Assert.Equal("docs/readme.txt", item.Name);
        Assert.Equal(9, item.SizeBytes);
        Assert.NotNull(item.ModifiedUtc);

        var extracted = Assert.IsType<FileStream>(await item.OpenSource(CancellationToken.None));
        var fullPath = extracted.Name;
        var tempRoot = Directory.GetParent(fullPath)!.Parent!.FullName;
        await using (extracted)
        {
            Assert.StartsWith("skycd-ace-index-", Path.GetFileName(tempRoot));
            Assert.Equal("hello ace", await File.ReadAllTextAsync(fullPath));
        }

        await set.DisposeAsync();
        Assert.False(File.Exists(fullPath));
        Assert.False(Directory.Exists(tempRoot));
    }

    [Fact]
    public async Task GetWorkItemsAsync_SkipsUnsafePaths_AndKeepsSafeEntries()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(
            ("safe.txt", "ok"),
            ("../escape.txt", "evil"),
            ("/absolute.txt", "evil"),
            ("folder/../../escape2.txt", "evil"),
            ("folder/nested.txt", "nested"));

        var set = await new AceArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        try
        {
            Assert.Equal(["folder/nested.txt", "safe.txt"], set.Items.Select(item => item.Name).Order());
            Assert.DoesNotContain(set.Items, item => item.Name.Contains("..", StringComparison.Ordinal));
        }
        finally
        {
            await set.DisposeAsync();
        }

        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_CompressedEntries_AreSkippedWithUnsupportedDiagnostic()
    {
        await using var archive = AceTestArchiveBuilder.CreateStoredAndCompressed(
            ("stored.txt", "plain"),
            ("packed.bin", new string('x', 4096)));

        var plugin = new AceArchiveIndexPlugin();
        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        try
        {
            Assert.Equal("stored.txt", Assert.Single(set.Items).Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_AllCompressed_ContributesNothing_AndReportsUnsupported()
    {
        await using var archive = AceTestArchiveBuilder.CreateCompressed(method: 11, ("packed.bin", "abc"));

        var plugin = new AceArchiveIndexPlugin();
        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        var diagnostic = Assert.Single(plugin.LastDiagnostics);
        Assert.StartsWith("ACE_UNSUPPORTED", diagnostic);
        Assert.Contains("packed.bin", diagnostic);
    }

    [Fact]
    public async Task GetWorkItemsAsync_EncryptedEntries_AreSkippedWithEncryptedDiagnostic()
    {
        await using var archive = AceTestArchiveBuilder.CreateEncrypted(("secret.txt", "hidden"));

        var plugin = new AceArchiveIndexPlugin();
        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "locked.ace"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.StartsWith("ACE_ENCRYPTED", Assert.Single(plugin.LastDiagnostics));
    }

    [Fact]
    public async Task GetWorkItemsAsync_MultiVolumeParts_AreSkipped()
    {
        await using var archive = AceTestArchiveBuilder.CreateMultiVolume(("part.bin", "abc"));

        var plugin = new AceArchiveIndexPlugin();
        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "split.ace"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.StartsWith("ACE_UNSUPPORTED", Assert.Single(plugin.LastDiagnostics));
    }

    [Fact]
    public async Task GetWorkItemsAsync_BadEntryCrc_IsSkippedAsDamaged_WhileOtherEntriesExtract()
    {
        await using var bad = AceTestArchiveBuilder.CreateStoredWithBadEntryCrc(("broken.bin", "payload"));
        var plugin = new AceArchiveIndexPlugin();

        var set = await plugin.GetWorkItemsAsync(RequestFor(bad.ToArray(), "sample.ace"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.StartsWith("ACE_DAMAGED", Assert.Single(plugin.LastDiagnostics));
    }

    [Fact]
    public async Task GetWorkItemsAsync_StoredEntriesWithRecoveryRecord_Extract()
    {
        await using var archive = AceTestArchiveBuilder.CreateWithRecoveryRecord(("kept.txt", "recovered"));

        var set = await new AceArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        Assert.Equal("kept.txt", Assert.Single(set.Items).Name);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        await using var broken = AceTestArchiveBuilder.CreateDamaged();
        var plugin = new AceArchiveIndexPlugin();

        var set = await plugin.GetWorkItemsAsync(RequestFor(broken.ToArray(), "broken.ace"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.StartsWith("ACE_DAMAGED", Assert.Single(plugin.LastDiagnostics));
    }

    [Fact]
    public async Task GetWorkItemsAsync_TruncatedArchive_ReturnsEmptySet_AndLeavesNoTempDirectory()
    {
        await using var truncated = AceTestArchiveBuilder.CreateTruncated(("cut.txt", "0123456789"));
        var plugin = new AceArchiveIndexPlugin();
        var before = TemporaryAceDirectories();

        var set = await plugin.GetWorkItemsAsync(RequestFor(truncated.ToArray(), "cut.ace"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.StartsWith("ACE_DAMAGED", Assert.Single(plugin.LastDiagnostics));
        Assert.Empty(TemporaryAceDirectories().Except(before));
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var plugin = new AceArchiveIndexPlugin(new AceIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        try
        {
            Assert.Equal("small.txt", Assert.Single(set.Items).Name);
            Assert.StartsWith("ACE_UNSUPPORTED", Assert.Single(plugin.LastDiagnostics));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryCountOverLimit_SkipsRemainingEntries()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        var plugin = new AceArchiveIndexPlugin(new AceIndexLimits(MaxEntries: 2));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"));

        try
        {
            Assert.Equal(["a.txt", "b.txt"], set.Items.Select(item => item.Name).Order());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonAceSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new AceArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream.ToArray(), "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates_AndLeavesNoTempDirectory()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = TemporaryAceDirectories();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AceArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive.ToArray(), "sample.ace"),
                cancellation.Token));

        Assert.Empty(TemporaryAceDirectories().Except(before));
    }

    [Fact]
    public async Task GetWorkItemsAsync_CancellationDuringExtraction_LeavesNoTempDirectory()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"));
        using var cancellation = new CancellationTokenSource();
        var before = TemporaryAceDirectories();
        var request = new IndexWorkItemRequest
        {
            FileName = "sample.ace",
            SizeBytes = archive.Length,
            OpenSource = _ =>
            {
                cancellation.Cancel();
                return Task.FromResult<Stream>(new MemoryStream(archive.ToArray()));
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AceArchiveIndexPlugin().GetWorkItemsAsync(request, cancellation.Token));

        Assert.Empty(TemporaryAceDirectories().Except(before));
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        await using var archive = AceTestArchiveBuilder.CreateStored(("images/photo.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new AceArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive.ToArray(), "sample.ace"));

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
        await using var archive = AceTestArchiveBuilder.CreateStored(("a.txt", "a"));
        var sequence = new IndexWorkItemSequence(new MediaExtensionManager([]), [new AceArchiveIndexPlugin()]);

        Assert.False(sequence.HasMetadataProviders);
        var indexed = await sequence.ProcessAsync(RequestFor(archive.ToArray(), "sample.ace"));

        Assert.Empty(indexed);
    }

    private static IndexWorkItemRequest RequestFor(byte[] bytes, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = bytes.Length,
        OpenSource = _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false))
    };

    private static HashSet<string> TemporaryAceDirectories() =>
        Directory.EnumerateDirectories(Path.GetTempPath(), "skycd-ace-index-*").ToHashSet(StringComparer.Ordinal);

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
