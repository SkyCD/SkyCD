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
using SkyCD.Plugin.FileFormat.Vhd.Models;

namespace SkyCD.Plugin.FileFormat.Vhd.Tests;

public sealed class VhdImageIndexTests
{
    [Fact]
    public void AssemblyDiscovery_RegistersFileFormatAndWorkItemProviders_OnSameInstance()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(VhdImageIndexPlugin).Assembly);

        var workItems = Assert.Single(plugin.Capabilities.OfType<IIndexWorkItemProvider>());
        var fileFormat = Assert.Single(plugin.Capabilities.OfType<IFileFormatPluginCapability>());

        Assert.Same(workItems, fileFormat);
        Assert.Equal("skycd-vhd", fileFormat.SupportedFormat.FormatId);
    }

    [Theory]
    [InlineData("vhd")]
    [InlineData("vhdx")]
    public async Task GetWorkItemsAsync_ListsContainedFiles_WithoutExtractingAny(string extension)
    {
        using var directory = TempVhdDirectory.Create();
        var disk = VhdTestImage.CreateFatVolume(("folder/notes.txt", "hello"));
        var imagePath = directory.Write($"disk.{extension}", Build(extension, disk));
        var before = IndexDirectories();

        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath));

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

    [Theory]
    [InlineData("vhd")]
    [InlineData("vhdx")]
    public async Task GetWorkItemsAsync_SeekableStream_WithoutSourcePath_Works(string extension)
    {
        var disk = VhdTestImage.CreateFatVolume(("notes.txt", "stream"));
        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Build(extension, disk), $"disk.{extension}"));

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

    // DiscUtils can only author VHD differencing images, so parent resolution is exercised for VHD.
    [Theory]
    [InlineData("vhd")]
    public async Task GetWorkItemsAsync_DifferencingImage_ResolvesParentNextToImage(string extension)
    {
        using var directory = TempVhdDirectory.Create();
        var parentContent = VhdTestImage.CreateFatVolume(("photo.txt", "backed"));
        var parentPath = directory.Resolve($"base.{extension}");
        WriteImageFile(extension, parentPath, parentContent);
        var childPath = directory.Resolve($"child.{extension}");
        CreateDifferencing(parentPath, childPath);

        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(RequestForPath(childPath));

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

    [Theory]
    [InlineData("vhd")]
    public async Task GetWorkItemsAsync_MissingParent_ReturnsEmptySet(string extension)
    {
        using var directory = TempVhdDirectory.Create();
        var parentContent = VhdTestImage.CreateFatVolume(("photo.txt", "backed"));
        var parentPath = directory.Resolve($"base.{extension}");
        WriteImageFile(extension, parentPath, parentContent);
        var childPath = directory.Resolve($"child.{extension}");
        CreateDifferencing(parentPath, childPath);
        File.Delete(parentPath);

        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(RequestForPath(childPath));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Theory]
    [InlineData("vhd")]
    public async Task GetWorkItemsAsync_ParentOutsideImageDirectory_ReturnsEmptySet(string extension)
    {
        using var parentDirectory = TempVhdDirectory.Create();
        using var childDirectory = TempVhdDirectory.Create();
        var parentContent = VhdTestImage.CreateFatVolume(("photo.txt", "backed"));
        var parentPath = parentDirectory.Write($"base.{extension}", Build(extension, parentContent));
        var childPath = childDirectory.Resolve($"child.{extension}");
        CreateDifferencing(parentPath, childPath);

        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(RequestForPath(childPath));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_DamagedImage_ReturnsEmptySet()
    {
        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes("not a vhd image"), "disk.vhd"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_EntryOverLimit_IsSkipped_WhileSafeEntryIsReturned()
    {
        using var directory = TempVhdDirectory.Create();
        var disk = VhdTestImage.CreateFatVolume(("small.txt", "ok"), ("large.txt", new string('x', 64)));
        var imagePath = directory.Write("disk.vhd", VhdTestImage.BuildVhd(disk));
        var plugin = new VhdImageIndexPlugin(new VhdIndexLimits(MaxEntries: 100, MaxEntryBytes: 8,
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
    public async Task GetWorkItemsAsync_NonVhdSource_ReturnsEmptySet()
    {
        var set = await new VhdImageIndexPlugin().GetWorkItemsAsync(
            RequestForStream(Encoding.UTF8.GetBytes("plain text"), "notes.txt"));

        Assert.Empty(set.Items);
        await set.DisposeAsync();
    }

    [Fact]
    public async Task GetWorkItemsAsync_Cancellation_Propagates()
    {
        using var directory = TempVhdDirectory.Create();
        var disk = VhdTestImage.CreateFatVolume(("a.txt", "a"), ("b.txt", "b"));
        var imagePath = directory.Write("disk.vhd", VhdTestImage.BuildVhd(disk));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new VhdImageIndexPlugin().GetWorkItemsAsync(RequestForPath(imagePath), cancellation.Token));
    }

    private static byte[] Build(string extension, byte[] diskContent) =>
        extension == "vhdx" ? VhdTestImage.BuildVhdx(diskContent) : VhdTestImage.BuildVhd(diskContent);

    private static void WriteImageFile(string extension, string path, byte[] diskContent)
    {
        if (extension == "vhdx")
        {
            VhdTestImage.WriteVhdxFile(path, diskContent);
        }
        else
        {
            VhdTestImage.WriteVhdFile(path, diskContent);
        }
    }

    private static void CreateDifferencing(string parentPath, string childPath) =>
        VhdTestImage.CreateVhdDifferencing(parentPath, childPath);

    private static string[] IndexDirectories() =>
        Directory.GetDirectories(Path.GetTempPath(), "skycd-vhd-index-*");

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
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
