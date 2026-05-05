using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Zip;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class ZipArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeZip_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-zip" && format.Extensions.Contains(".zip"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-zip");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyZipFormat()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-zip",
            Target = stream,
            Payload = new { }
        }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesDeepAndUnicodeEntries_WithMetadata()
    {
        var service = new FileFormatManager(CreateCatalog().GetCapabilities<IFileFormatPluginCapability>());
        await using var zipStream = CreateFixtureZip();

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-zip",
            Source = zipStream
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);

        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/įrašas.txt"));
        Assert.Contains(rows, row => Equals(row["kind"], "file") && Equals(row["sizeBytes"], "5"));
        Assert.Contains(rows, row => row.ContainsKey("modifiedUtc"));
    }

    private static MemoryStream CreateFixtureZip()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var first = archive.CreateEntry("root/deep/įrašas.txt");
            using (var writer = new StreamWriter(first.Open(), new UTF8Encoding(false), leaveOpen: false))
            {
                writer.Write("hello");
            }

            var second = archive.CreateEntry("root/notes.txt");
            using var secondWriter = new StreamWriter(second.Open(), new UTF8Encoding(false), leaveOpen: false);
            secondWriter.Write("abc");
        }

        stream.Position = 0;
        return stream;
    }

    private static PluginManager CreateCatalog()
    {
        var plugin = new ZipArchiveIndexPlugin();
        var catalog = PluginManagerTestFactory.Create();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.zip",
                Name = "ZipArchiveIndexPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}
