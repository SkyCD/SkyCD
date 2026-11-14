using System;
using System.IO;
using System.IO.Compression;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using Xunit;

namespace SkyCD.Plugin.Librer.Tests;

public sealed class LibrerDetectionTests
{
    private readonly LibrerPlugin _plugin = new();

    [Fact]
    public void SupportedFormat_ReturnsExpectedMetadata()
    {
        var descriptor = _plugin.SupportedFormat;

        Assert.Equal("librer", descriptor.FormatId);
        Assert.Contains(".librer_data", descriptor.Extensions);
        Assert.Contains(".librer_data_compr", descriptor.Extensions);
        Assert.Contains(".dat", descriptor.Extensions);
        Assert.True(descriptor.CanRead);
        Assert.False(descriptor.CanWrite);
    }

    [Fact]
    public void CanHandleContent_ValidLibrerFile_ReturnsTrue()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"librer_det_{Guid.NewGuid():N}.librer_data");
        try
        {
            LibrerTestHelper.CreateSampleLibrerCatalog(tempFile);
            using var stream = File.OpenRead(tempFile);

            var canHandle = _plugin.CanHandleContent(stream, tempFile);

            Assert.True(canHandle);
            Assert.Equal(0, stream.Position);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void CanHandleContent_NonZipFile_ReturnsFalse()
    {
        using var stream = new MemoryStream("Hello world this is not a zip"u8.ToArray());

        var canHandle = _plugin.CanHandleContent(stream, "sample.dat");

        Assert.False(canHandle);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_ZipWithoutLibrerEntries_ReturnsFalse()
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("some_random_file.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("not librer");
        }
        memoryStream.Position = 0;

        var canHandle = _plugin.CanHandleContent(memoryStream, "random.zip");

        Assert.False(canHandle);
        Assert.Equal(0, memoryStream.Position);
    }

    [Fact]
    public void CanHandleContent_EmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream();

        var canHandle = _plugin.CanHandleContent(stream, "empty.librer_data");

        Assert.False(canHandle);
    }
}
