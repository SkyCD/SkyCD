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
using SkyCD.Plugin.FileFormat.Qcow2.Models;

namespace SkyCD.Plugin.FileFormat.Qcow2.Tests;

public sealed class Qcow2ImageIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(Qcow2ImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-qcow2", fileFormat.SupportedFormat.FormatId);
    }

    [Fact]
    public async Task GetWorkItemsAsync_ListsContainedFiles_WithoutExtractingAny()
    {
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("folder/notes.txt", "hello"));
        var imagePath = directory.Write("disk.qcow2", Qcow2TestImage.Build(disk));
        var before = IndexDirectories();

        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

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
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("notes.txt", "compressed payload"));
        var imagePath = directory.Write("disk.qcow2", Qcow2TestImage.Build(disk, compress: true));

        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

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
    public async Task GetWorkItemsAsync_SeekableStream_WithoutSourcePath_Works()
    {
        var disk = Qcow2TestImage.CreateFatDisk(("notes.txt", "stream"));
        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Qcow2TestImage.Build(disk)));

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
    public async Task GetWorkItemsAsync_BackingFile_ResolvesNextToImage()
    {
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("photo.txt", "backed"));
        directory.Write("base.qcow2", Qcow2TestImage.Build(disk));
        var overlayPath = directory.Write("overlay.qcow2",
            Qcow2TestImage.BuildOverlay(disk.Length, "base.qcow2"));

        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(RequestForPath(overlayPath));

        try
        {
            var item = Assert.Single(set.Items);
            Assert.EndsWith("photo.txt", item.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("backed", await ReadItemAsync(item));
        }
        finally
        {
            await set.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetWorkItemsAsync_BackingFileOutsideImageDirectory_ReturnsEmptySet()
    {
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("photo.txt", "backed"));
        directory.Write("base.qcow2", Qcow2TestImage.Build(disk));
        var overlayPath = directory.Write("overlay.qcow2",
            Qcow2TestImage.BuildOverlay(disk.Length, "../base.qcow2"));

        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(RequestForPath(overlayPath));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_MissingBackingFile_ReturnsEmptySet()
    {
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("photo.txt", "backed"));
        var overlayPath = directory.Write("overlay.qcow2",
            Qcow2TestImage.BuildOverlay(disk.Length, "missing.qcow2"));

        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(RequestForPath(overlayPath));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EncryptedImage_ReturnsEmptySet()
    {
        var disk = Qcow2TestImage.CreateFatDisk(("notes.txt", "secret"));

        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Qcow2TestImage.Build(disk, cryptMethod: 1)));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedImage_ReturnsEmptySet()
    {
        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes("not a qcow2 image")));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var imagePath = directory.Write("disk.qcow2", Qcow2TestImage.Build(disk));
        var plugin = new Qcow2ImageIndexPlugin(new Qcow2IndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
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
    public async Task GetWorkItemsAsync_NonQcow2Source_ReturnsEmptySet()
    {
        var set = await new Qcow2ImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes("plain text"), "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var directory = TempQcow2Directory.Create();
        var disk = Qcow2TestImage.CreateFatDisk(("a.txt", "a"), ("b.txt", "b"));
        var imagePath = directory.Write("disk.qcow2", Qcow2TestImage.Build(disk));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new Qcow2ImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath), cancellation.Token));
    }

    private static string[] IndexDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "skycd-qcow2-index-*");

    private static IndexWorkItemRequest RequestForPath(string imagePath) => new()
    {
        FileName = Path.GetFileName(imagePath),
        SizeBytes = new FileInfo(imagePath).Length,
        SourcePath = imagePath,
        OpenSource = _ => Task.FromResult<Stream>(
            new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
    };

    private static IndexWorkItemRequest RequestForStream(byte[] image, string fileName = "disk.qcow2") => new()
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
