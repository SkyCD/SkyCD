using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Cab;
using SkyCD.Plugin.Metadata.Cab.Tests;
using SkyCD.Plugin.Host.Indexing;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.Cab.Models;

namespace SkyCD.Plugin.Host.Tests;

public sealed class CabArchiveIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersWorkItemProvider()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(CabArchiveIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());

        Assert.IsType<CabArchiveIndexPlugin>(workItems);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GetWorkItemsAsync_ExtractsStoredAndMsZipEntries(int compressionType)
    {
        using var archive = CabTestArchiveBuilder.Create(compressionType,
        [
            new CabTestArchiveBuilder.Member("docs/readme.txt", "hello cabinet"u8.ToArray()),
            new CabTestArchiveBuilder.Member("docs/deep/data.bin", [1, 2, 3, 4, 5])
        ]);

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.cab"));

        var tempRoots = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            Assert.Equal(["docs/deep/data.bin", "docs/readme.txt"], set.Items.Select(item => item.Name).Order());
            foreach (var item in set.Items.Where(item => item.Name.EndsWith("readme.txt", StringComparison.Ordinal)))
            {
                await using var stream = await item.OpenSource(CancellationToken.None);
                var path = ((FileStream)stream).Name;
                tempRoots.Add(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName);
                using var reader = new StreamReader(stream);
                Assert.Equal("hello cabinet", await reader.ReadToEndAsync());
            }

            Assert.Contains(tempRoots, root => Path.GetFileName(root).StartsWith("skycd-cab-index-", StringComparison.Ordinal));
        }
        finally
        {
            await set.DisposeAsync();
        }

        Assert.All(tempRoots, root => Assert.False(Directory.Exists(root)));
    }

    [Fact]
    public async Task GetWorkItemsAsync_MsZipBlocksLargerThan32KiB_AreDecodedAcrossBlockBoundaries()
    {
        var first = Encoding.UTF8.GetBytes(new string('a', 40_000));
        var second = Encoding.UTF8.GetBytes(new string('b', 70_000));
        using var archive = CabTestArchiveBuilder.CreateMsZip(
            new List<CabTestArchiveBuilder.Member>
            {
                new("a.txt", first),
                new("b.txt", second)
            }, new CabTestArchiveBuilder.Options { IncludeChecksum = true });

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "blocks.cab"));
        try
        {
            var b = set.Items.Single(item => item.Name == "b.txt");
            await using var stream = await b.OpenSource(CancellationToken.None);
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            Assert.Equal(second, memory.ToArray());
            Assert.Equal(second.Length, b.SizeBytes);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_MultipleFolders_ExtractsEveryFolder()
    {
        var members = new List<CabTestArchiveBuilder.Member>
        {
            new("one.txt", "o"u8.ToArray()),
            new("two.txt", "t"u8.ToArray()),
            new("three.txt", "t"u8.ToArray())
        };
        using var archive = CabTestArchiveBuilder.CreateSplit(members, new CabTestArchiveBuilder.Options { FolderCount = 3 });

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "folders.cab"));
        try
        {
            Assert.Equal(["one.txt", "three.txt", "two.txt"], set.Items.Select(item => item.Name).Order());
            foreach (var item in set.Items)
            {
                await using var stream = await item.OpenSource(CancellationToken.None);
                using var reader = new StreamReader(stream);
                Assert.Equal(item.Name[..1], await reader.ReadToEndAsync());
            }
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_SkipsUnsafeNames_AndNeverWritesOutsideTheTempDirectory()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(
            new List<CabTestArchiveBuilder.Member>
            {
                new("safe.txt", "ok"u8.ToArray()),
                new("../escape.txt", "evil"u8.ToArray()),
                new("/absolute.txt", "evil"u8.ToArray()),
                new("C:\\windows\\evil.txt", "evil"u8.ToArray()),
                new("folder/../../escape2.txt", "evil"u8.ToArray()),
                new("folder/nested.txt", "nested"u8.ToArray())
            }, new CabTestArchiveBuilder.Options());

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.cab"));

        Assert.Equal(["folder/nested.txt", "safe.txt"], set.Items.Select(item => item.Name).Order());
        string tempRoot;
        await using (var extracted = await set.Items.Single(item => item.Name == "safe.txt").OpenSource(CancellationToken.None))
        {
            tempRoot = Path.GetDirectoryName(((FileStream)extracted).Name)!;
            Assert.Equal("safe.txt", Path.GetFileName(((FileStream)extracted).Name));
        }

        Assert.Empty(Directory.EnumerateFiles(tempRoot, "escape*.txt", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(tempRoot, "evil.txt", SearchOption.AllDirectories));
        await set.DisposeAsync();
        Assert.False(Directory.Exists(tempRoot));
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task GetWorkItemsAsync_DuplicateNames_KeepTheFirstEntry()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(
            new List<CabTestArchiveBuilder.Member>
            {
                new("dup.txt", "first"u8.ToArray()),
                new("dup.txt", "second"u8.ToArray())
            }, new CabTestArchiveBuilder.Options());

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "dup.cab"));
        try
        {
            var item = Assert.Single(set.Items);
            await using var stream = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.Equal("first", await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedArchive_ReturnsEmptySet()
    {
        using var broken = CabTestArchiveBuilder.CreateDamaged();

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(broken, "broken.cab"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_TruncatedArchive_ReturnsEmptySetOrSafeItems_WithoutThrowing()
    {
        using var truncated = CabTestArchiveBuilder.CreateTruncated(("kept.txt", new string('k', 512)));

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(truncated, "truncated.cab"));

        try
        {
            foreach (var item in set.Items)
            {
                await using var stream = await item.OpenSource(CancellationToken.None);
                Assert.Equal(item.SizeBytes, stream.Length);
            }
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedCompression_IsListedButNotExtracted()
    {
        var members = new[]
        {
            new CabTestArchiveBuilder.Member("packed.bin", new byte[128]),
            new CabTestArchiveBuilder.Member("other.bin", new byte[16])
        };
        using var archive = CabTestArchiveBuilder.Create(2, members);

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "quantum.cab"));
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
    public void UnsupportedEntryException_CarriesTypedDiagnostic()
    {
        var exception = new SkyCD.Plugin.Metadata.Cab.Exceptions.CabUnsupportedEntryException("packed.bin", 3);
        Assert.Equal("packed.bin", exception.EntryName);
        Assert.Equal(3, exception.CompressionType);
    }

    [Theory]
    [InlineData("prev.cab", null)]
    [InlineData(null, "next.cab")]
    public async Task GetWorkItemsAsync_ContinuationCabinet_ReturnsEmptySetInsteadOfPartialListing(string? prev, string? next)
    {
        using var archive = CabTestArchiveBuilder.CreateStored(
            new List<CabTestArchiveBuilder.Member> { new("part.txt", "data"u8.ToArray()) },
            new CabTestArchiveBuilder.Options { PrevCabinet = prev, NextCabinet = next });

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "multi.cab"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var plugin = new CabArchiveIndexPlugin(new CabIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "sample.cab"));

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
    public async Task GetWorkItemsAsync_TotalBudgetLimit_StopsAddingEntries()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("a.txt", "aaaa"), ("b.txt", "bbbb"), ("c.txt", "cccc"));
        var plugin = new CabArchiveIndexPlugin(new CabIndexLimits(MaxEntries: 100, MaxEntryBytes: 1024,
            MaxTotalBytes: 9));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "sample.cab"));

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
    public async Task GetWorkItemsAsync_EntryCountLimit_StopsAtTheBound()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"), ("c.txt", "c"));
        var plugin = new CabArchiveIndexPlugin(new CabIndexLimits(MaxEntries: 2));

        var set = await plugin.GetWorkItemsAsync(RequestFor(archive, "sample.cab"));

        try
        {
            Assert.Equal(2, set.Items.Count);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonCabSource_ReturnsEmptySet()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("plain text"));

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(stream, "notes.cab"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonCabExtension_ReturnsEmptySet()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("a.txt", "a"));

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSeekableSource_IsBuffered()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("buffered.txt", "streamed"));
        using var nonSeekable = new NonSeekableStream(archive.ToArray());

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(nonSeekable, "stream.cab"));
        try
        {
            var item = Assert.Single(set.Items);
            await using var stream = await item.OpenSource(CancellationToken.None);
            using var reader = new StreamReader(stream);
            Assert.Equal("streamed", await reader.ReadToEndAsync());
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_PropagatesAndRemovesTempDirectories()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "skycd-cab-index-*");
        using var archive = CabTestArchiveBuilder.CreateStored(
            ("a.txt", new string('a', 4096)), ("b.txt", new string('b', 4096)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "sample.cab"), cancellation.Token));

        var after = Directory.GetDirectories(Path.GetTempPath(), "skycd-cab-index-*");
        Assert.Equal(before.Length, after.Length);
    }

    [Fact]
    public async Task GetWorkItemsAsync_Dispose_RemovesTheTemporaryDirectory_EvenWhenFilesAreReadOnly()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("readonly.txt", "content"));
        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(RequestFor(archive, "ro.cab"));
        var item = Assert.Single(set.Items);
        string path;
        await using (var stream = await item.OpenSource(CancellationToken.None))
        {
            path = ((FileStream)stream).Name;
            File.SetAttributes(path, FileAttributes.ReadOnly);
        }

        await set.DisposeAsync();

        Assert.False(File.Exists(path));
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task GetWorkItemsAsync_ReportsProgressUpToOneHundred()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("a.txt", "a"), ("b.txt", "b"));
        var progress = new RecordingProgress();

        var set = await new CabArchiveIndexPlugin().GetWorkItemsAsync(new IndexWorkItemRequest
        {
            FileName = "progress.cab",
            SizeBytes = archive.Length,
            OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(archive)),
            Progress = progress
        });

        try
        {
            Assert.Equal(100, progress.Last);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sequence_RunsEnabledMetadataProviders_ForContributedItems()
    {
        using var archive = CabTestArchiveBuilder.CreateStored(("images/photo.txt", "metadata-source"));
        var manager = new MediaExtensionManager([new MediaProvider("test", "1.0", new TestMetadataCapability())]);
        var sequence = new IndexWorkItemSequence(manager, [new CabArchiveIndexPlugin()]);

        var indexed = await sequence.ProcessAsync(RequestFor(archive, "sample.cab"));

        var entry = Assert.Single(indexed);
        Assert.Equal("images/photo.txt", entry.Name);
        var providerResult = Assert.Single(entry.Results);
        Assert.False(providerResult.Failed);
        Assert.Contains(providerResult.Metadata!.Values, value =>
            value.Key == "indexed" && value.Text == "yes");
    }

    private static IndexWorkItemRequest RequestFor(Stream stream, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = stream.CanSeek ? stream.Length : 0,
        OpenSource = _ => Task.FromResult<Stream>(new NonDisposingStream(stream))
    };

    private sealed class RecordingProgress : IProgress<int>
    {
        public int? Last { get; private set; }

        public void Report(int value) => Last = value;
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

    /// <summary>Forward-only stream used to verify the plugin buffers non-seekable cabinets.</summary>
    private sealed class NonSeekableStream(byte[] content) : Stream
    {
        private readonly MemoryStream inner = new(content);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
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
