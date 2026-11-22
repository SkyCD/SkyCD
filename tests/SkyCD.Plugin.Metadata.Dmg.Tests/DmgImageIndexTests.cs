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
using SkyCD.Plugin.Metadata.Dmg.Models;

namespace SkyCD.Plugin.Metadata.Dmg.Tests;

public sealed class DmgImageIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersWorkItemProvider_WithoutFileFormatCapability()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(DmgImageIndexPlugin).Assembly);

        Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        Assert.Empty(plugin.Capabilities.OfType<IFileFormatPluginCapability>());
    }

    [Fact]
    public async Task GetWorkItemsAsync_ListsContainedFiles_WithoutExtractingAny()
    {
        using var directory = TempDmgDirectory.Create();
        var disk = DmgTestImage.CreateFatDisk(("folder/notes.txt", "hello"));
        var imagePath = directory.Write("disk.dmg", DmgTestImage.Build(disk));
        var before = IndexDirectories();

        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        var indexDirectory = Assert.Single(IndexDirectories().Except(before));
        try
        {
            var item = Assert.Single(set.Items);
            Assert.EndsWith("notes.txt", item.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(5, item.SizeBytes);
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
    public async Task GetWorkItemsAsync_CompressedImage_ExtractsStoredContent()
    {
        using var directory = TempDmgDirectory.Create();
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "compressed payload"));
        var imagePath = directory.Write("disk.dmg", DmgTestImage.Build(disk, compress: true));

        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("compressed payload", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_WholeDiskFileSystem_IsReadable()
    {
        using var directory = TempDmgDirectory.Create();
        var volume = DmgTestImage.CreateFatVolume(("notes.txt", "floppy"));
        var imagePath = directory.Write("disk.dmg", DmgTestImage.Build(volume, compress: true));

        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("floppy", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_SeekableStream_WithoutSourcePath_Works()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "stream"));
        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(DmgTestImage.Build(disk)));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.Equal("stream", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_EncryptedImage_ReturnsEmptySet()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "secret"));

        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(DmgTestImage.Build(disk, encrypted: true)));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_SegmentedImage_ReturnsEmptySet()
    {
        var disk = DmgTestImage.CreateFatDisk(("notes.txt", "segment"));

        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(DmgTestImage.Build(disk, segmentNumber: 1, segmentCount: 2)));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedImage_ReturnsEmptySet()
    {
        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes("not a dmg image")));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var directory = TempDmgDirectory.Create();
        var disk = DmgTestImage.CreateFatDisk(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var imagePath = directory.Write("disk.dmg", DmgTestImage.Build(disk));
        var plugin = new DmgImageIndexPlugin(new DmgIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
            MaxTotalBytes: 1024));

        var set = await plugin.GetWorkItemsAsync(RequestForPath(imagePath));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.EndsWith("small.txt", item.Name, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_NonDmgSource_ReturnsEmptySet()
    {
        var set = await new DmgImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes("plain text"), "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var directory = TempDmgDirectory.Create();
        var disk = DmgTestImage.CreateFatDisk(("a.txt", "a"), ("b.txt", "b"));
        var imagePath = directory.Write("disk.dmg", DmgTestImage.Build(disk));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new DmgImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath), cancellation.Token));
    }

    private static string[] IndexDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "skycd-dmg-index-*");

    private static IndexWorkItemRequest RequestForPath(string imagePath) => new()
    {
        FileName = Path.GetFileName(imagePath),
        SizeBytes = new FileInfo(imagePath).Length,
        SourcePath = imagePath,
        OpenSource = _ => Task.FromResult<Stream>(
            new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    };

    private static IndexWorkItemRequest RequestForStream(byte[] image, string fileName = "disk.dmg") => new()
    {
        FileName = fileName,
        SizeBytes = image.Length,
        OpenSource = _ => Task.FromResult<Stream>(new MemoryStream(image))
    };

    private static async Task<string> ReadItemAsync(IndexWorkItem item)
    {
        await using var stream = await item.OpenSource(CancellationToken.None);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
