using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;

namespace SkyCD.Plugin.Vdi.Tests;

public sealed class VdiImageIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VdiImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-vdi", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public void Fixture_IsTheCommittedCatalogImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vdi", "catalog.vdi");
        Assert.True(File.Exists(path), $"Missing fixture: {path}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetWorkItemsAsync_ListsNestedFiles_WithoutExtractingAny(bool fixedImage)
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("folder/notes.txt", "hello"));
        var imagePath = directory.Write("disk.vdi", Build(disk, fixedImage));
        var before = IndexDirectories();

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        var indexDirectory = Assert.Single(IndexDirectories().Except(before));
        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("FOLDER/NOTES.TXT", item.Name);
            Assert.Equal(5, item.SizeBytes);
            Assert.NotNull(item.ModifiedUtc);
            Assert.Empty(Directory.EnumerateFiles(indexDirectory, "*", SearchOption.AllDirectories));

            Assert.Equal("hello", await ReadItemAsync(item));
            Assert.Empty(Directory.EnumerateFiles(indexDirectory, "*", SearchOption.AllDirectories));
        }
        finally
        {
            await set.DisposeAsync();
        }

        Assert.False(Directory.Exists(indexDirectory));
    }

    [Fact]
    public async Task GetWorkItemsAsync_ExtractsOnlyOneFileAtATime()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("a.txt", "first"), ("b.txt", "second"));
        var imagePath = directory.Write("disk.vdi", VdiTestImage.BuildDynamic(disk));
        var before = IndexDirectories();

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        var indexDirectory = Assert.Single(IndexDirectories().Except(before));
        try
        {
            Assert.Equal(2, set.Items.Count);
            await using (var first = await set.Items[0].OpenSource(CancellationToken.None))
            {
                Assert.Single(Directory.EnumerateFiles(indexDirectory));
                Assert.Equal("first", ReadAll(first));
            }

            Assert.Empty(Directory.EnumerateFiles(indexDirectory));
        }
        finally
        {
            await set.DisposeAsync();
        }

        Assert.False(Directory.Exists(indexDirectory));
    }

    [Fact]
    public async Task GetWorkItemsAsync_SeekableStreamWithoutSourcePath_Works()
    {
        var disk = VdiTestImage.CreateFatVolume(("notes.txt", "stream"));

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(VdiTestImage.BuildDynamic(disk), "disk.vdi"));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("NOTES.TXT", item.Name);
            Assert.Equal("stream", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonSeekableStream_IsBuffered()
    {
        var disk = VdiTestImage.CreateFatVolume(("notes.txt", "buffered"));
        var request = new IndexWorkItemRequest
        {
            FileName = "disk.vdi",
            SizeBytes = VdiTestImage.BuildDynamic(disk).Length,
            OpenSource = _ => Task.FromResult<Stream>(new ForwardOnlyStream(VdiTestImage.BuildDynamic(disk)))
        };

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(request);

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("buffered", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_SnapshotLayer_ReturnsEmptySet()
    {
        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(VdiTestImage.BuildWithHeader(imageFlags: 0x1), "snapshot.vdi"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EncryptedImage_ReturnsEmptySet()
    {
        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(VdiTestImage.BuildWithHeader(imageFlags: 0x4), "encrypted.vdi"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Theory]
    [InlineData("not a vdi image at all, just some text that is long enough to pass the length check")]
    [InlineData("")]
    public async Task GetWorkItemsAsync_UnrecognizedContent_ReturnsEmptySet(string content)
    {
        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes(content), "disk.vdi"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_TruncatedImage_CleansUpTemporaryDirectory()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("notes.txt", "content"));
        var image = VdiTestImage.BuildDynamic(disk);
        var truncated = image.AsSpan(0, 600).ToArray();
        var imagePath = directory.Write("disk.vdi", truncated);
        var before = IndexDirectories();

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
        Assert.Empty(IndexDirectories().Except(before));
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedHeaderVersion_ReturnsEmptySet()
    {
        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(VdiTestImage.BuildWithHeader(imageFlags: 0, version: 0x0001_0009), "future.vdi"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsupportedSectorSize_ReturnsEmptySet()
    {
        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(VdiTestImage.BuildWithSectorSize(2048), "odd-sector.vdi"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("SMALL.TXT", "ok"), ("LARGE.TXT", new string('x', 64)));
        var imagePath = directory.Write("disk.vdi", VdiTestImage.BuildDynamic(disk));
        var plugin = new VdiImageIndexPlugin(new VdiIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestForPath(imagePath));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("SMALL.TXT", item.Name);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_TotalLimit_BoundsListedItems()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("a.txt", "aaaa"), ("b.txt", "bbbb"), ("c.txt", "cccc"));
        var imagePath = directory.Write("disk.vdi", VdiTestImage.BuildDynamic(disk));
        var plugin = new VdiImageIndexPlugin(new VdiIndexLimits(MaxEntries: 100, MaxEntryBytes: 1024,
            MaxTotalBytes: 8));

        var set = await plugin.GetWorkItemsAsync(RequestForPath(imagePath));

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
    public async Task GetWorkItemsAsync_CancelledExtraction_LeavesNoTemporaryFile()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("wide.txt", new string('w', 4096)));
        var imagePath = directory.Write("disk.vdi", VdiTestImage.BuildDynamic(disk));
        var before = IndexDirectories();

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        var indexDirectory = Assert.Single(IndexDirectories().Except(before));
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => set.Items[0].OpenSource(cancellation.Token));
            Assert.Empty(Directory.EnumerateFiles(indexDirectory, "*", SearchOption.AllDirectories));
        }
        finally
        {
            await set.DisposeAsync();
        }

        Assert.False(Directory.Exists(indexDirectory));
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates_AndCleansUp()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("a.txt", "a"), ("b.txt", "b"));
        var imagePath = directory.Write("disk.vdi", VdiTestImage.BuildDynamic(disk));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var before = IndexDirectories();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath), cancellation.Token));

        Assert.Empty(IndexDirectories().Except(before));
    }

    [Fact]
    public async Task GetWorkItemsAsync_UnsafeEntryNames_AreIgnored()
    {
        using var directory = TempVdiDirectory.Create();
        var disk = VdiTestImage.CreateFatVolume(("SAFE.TXT", "safe"));
        var imagePath = directory.Write("disk.vdi", VdiTestImage.BuildDynamic(disk));

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        try
        {
            Assert.All(set.Items, item => Assert.DoesNotContain("..", item.Name, StringComparison.Ordinal));
            Assert.All(set.Items, item => Assert.DoesNotContain('\\', item.Name));
            Assert.Contains(set.Items, item => item.Name == "SAFE.TXT");
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_CommittedFixture_ListsFoldersAndFiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Vdi", "catalog.vdi");

        var set = await new VdiImageIndexPlugin().GetWorkItemsAsync(RequestForPath(path));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal(34, item.SizeBytes);
            Assert.Equal(new string('c', 34), await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    private static byte[] Build(byte[] diskContent, bool fixedImage) =>
        fixedImage ? VdiTestImage.BuildFixed(diskContent) : VdiTestImage.BuildDynamic(diskContent);

    private static string[] IndexDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "skycd-vdi-index-*");

    private static IndexWorkItemRequest RequestForPath(string imagePath) => new()
    {
        FileName = Path.GetFileName(imagePath),
        SizeBytes = new FileInfo(imagePath).Length,
        SourcePath = imagePath,
        OpenSource = _ => Task.FromResult<Stream>(
            new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    };

    private static IndexWorkItemRequest RequestForStream(byte[] image, string fileName) => new()
    {
        FileName = fileName,
        SizeBytes = image.Length,
        OpenSource = _ => Task.FromResult<Stream>(new MemoryStream(image))
    };

    private static async Task<string> ReadItemAsync(IndexWorkItem item)
    {
        await using var stream = await item.OpenSource(CancellationToken.None);
        return ReadAll(stream);
    }

    private static string ReadAll(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A stream that reports no random access, like a network or pipe source.</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream inner;
        private readonly int maxChunk;

        public ForwardOnlyStream(byte[] content)
        {
            inner = new MemoryStream(content, writable: false);
            maxChunk = Math.Max(1, content.Length / 8);
        }

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

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, Math.Min(count, maxChunk));

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
