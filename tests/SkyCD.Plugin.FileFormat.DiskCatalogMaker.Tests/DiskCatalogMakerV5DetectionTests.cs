using System;
using System.IO;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5DetectionTests
{
    private readonly DiskCatalogMakerV5Plugin plugin = new();

    [Fact]
    public void SupportedFormat_HasExpectedMetadata()
    {
        var format = plugin.SupportedFormat;
        Assert.Equal("diskcatalogmaker-v5", format.FormatId);
        Assert.Equal("DiskCatalogMaker Catalog v5 (*.dcmf, *.dcmd)", format.DisplayName);
        Assert.Contains(".dcmf", format.Extensions);
        Assert.Contains(".dcmd", format.Extensions);
        Assert.True(format.CanRead);
        Assert.False(format.CanWrite);
        Assert.True(format.SupportsFolder);
    }

    [Fact]
    public void CanHandleContent_RecognizesCatalogHeaderAndPreservesPosition()
    {
        var payload = new DiskCatalogMakerV5FixtureBuilder().Build();
        var prefixed = new byte[3 + payload.Length];
        payload.CopyTo(prefixed, 3);
        using var stream = new MemoryStream(prefixed);
        stream.Position = 3;

        Assert.True(plugin.CanHandleContent(stream, "catalog.dcmf"));
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void CanHandleContent_RejectsTextAndArbitraryCatalogFormats()
    {
        var text = "Name\tPath\tSize\r\nfile.txt\t/Volumes/Disk/file.txt\t10\r\n";
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
        {
            Assert.False(plugin.CanHandleContent(stream, "export.txt"));
            Assert.Equal(0, stream.Position);
        }

        var dicom = new byte[132];
        dicom[128] = (byte)'D';
        dicom[129] = (byte)'I';
        dicom[130] = (byte)'C';
        dicom[131] = (byte)'M';
        using (var stream = new MemoryStream(dicom))
        {
            Assert.False(plugin.CanHandleContent(stream, "scan.dcm"));
            Assert.Equal(0, stream.Position);
        }
    }

    [Fact]
    public void CanHandleContent_RejectsTruncatedPayload()
    {
        var payload = new DiskCatalogMakerV5FixtureBuilder().Build();
        using var stream = new MemoryStream(payload[..16]);

        Assert.False(plugin.CanHandleContent(stream, "catalog.dcmf"));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void CanHandleContent_RejectsWrongByteOrderMarker()
    {
        var payload = new DiskCatalogMakerV5FixtureBuilder().Build();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt16(payload, 10, 0x4245);
        using var stream = new MemoryStream(payload);

        Assert.False(plugin.CanHandleContent(stream, "catalog.dcmf"));
    }

    [Fact]
    public void CanHandleContent_RejectsUnexpectedHeaderSize()
    {
        var payload = new DiskCatalogMakerV5FixtureBuilder().Build();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt16(payload, 4, 16);
        using var stream = new MemoryStream(payload);

        Assert.False(plugin.CanHandleContent(stream, "catalog.dcmf"));
    }

    [Fact]
    public void CanHandleContent_RecognizesUnsupportedMajorVersionSoParserCanExplain()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder { MajorVersion = 6 };
        var payload = builder.Build();
        using var stream = new MemoryStream(payload);

        Assert.True(plugin.CanHandleContent(stream, "catalog.dcmf"));
    }

    [Fact]
    public void CanHandleFolder_RequiresStructurallyValidCatalogPayload()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            Assert.False(plugin.CanHandleFolder(root));

            File.WriteAllBytes(
                Path.Combine(root, "Catalog.dcm"),
                new DiskCatalogMakerV5FixtureBuilder().Build());
            Assert.True(plugin.CanHandleFolder(root));

            File.WriteAllBytes(Path.Combine(root, "Catalog.dcm"), [1, 2, 3, 4]);
            Assert.False(plugin.CanHandleFolder(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CanHandleFolder_RejectsMissingDirectory()
    {
        Assert.False(plugin.CanHandleFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        Assert.False(plugin.CanHandleFolder(string.Empty));
    }

    [Fact]
    public void FileFormatManager_ResolvesV5ByExtensionAndPackageFolder()
    {
        var manager = new FileFormatManager(
        [
            new DiskCatalogMakerPlugin(),
            new DiskCatalogMakerV5Plugin()
        ]);

        Assert.Equal("diskcatalogmaker-v5", manager.GetInstanceFor("catalog.dcmf").SupportedFormat.FormatId);
        Assert.Equal("diskcatalogmaker-v5", manager.GetInstanceFor("catalog.dcmd").SupportedFormat.FormatId);

        var root = CreateTemporaryDirectory();
        try
        {
            File.WriteAllBytes(
                Path.Combine(root, "Catalog.dcm"),
                new DiskCatalogMakerV5FixtureBuilder().Build());

            var capability = manager.GetInstanceFor(root);
            Assert.Equal("diskcatalogmaker-v5", capability.SupportedFormat.FormatId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "skycd-dcm5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
