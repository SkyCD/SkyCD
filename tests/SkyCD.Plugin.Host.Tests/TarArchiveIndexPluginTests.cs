using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Tar;

namespace SkyCD.Plugin.Host.Tests;

public class TarArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeTarVariants_ButSaveFormatsDoNot()
    {
        var service = new FileFormatRoutingService(CreateCatalog());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format =>
            format.FormatId == "skycd-tar" &&
            format.Extensions.Contains(".tar") &&
            format.Extensions.Contains(".tar.gz") &&
            format.Extensions.Contains(".tgz"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-tar");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyTarFormat()
    {
        var service = new FileFormatRoutingService(CreateCatalog());
        await using var stream = new MemoryStream();

        var exception = await Assert.ThrowsAsync<FileFormatRoutingException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-tar",
                Target = stream,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_IndexesTarAndGzipTar_WithPathNormalizationAndMetadata()
    {
        var service = new FileFormatRoutingService(CreateCatalog());

        await using var tarStream = CreateTarFixture(false);
        var tarResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-tar",
            Source = tarStream
        });
        Assert.True(tarResult.Success);
        var tarRows = Assert.IsType<List<Dictionary<string, object?>>>(tarResult.Payload);
        Assert.Contains(tarRows, row => Equals(row["fullPath"], "root/deep/įrašas.txt"));

        await using var tarGzStream = CreateTarFixture(true);
        var tarGzResult = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-tar",
            Source = tarGzStream
        });
        Assert.True(tarGzResult.Success);
        var tarGzRows = Assert.IsType<List<Dictionary<string, object?>>>(tarGzResult.Payload);
        Assert.Contains(tarGzRows, row => Equals(row["kind"], "file") && Equals(row["sizeBytes"], "5"));
    }

    private static MemoryStream CreateTarFixture(bool gzip)
    {
        var output = new MemoryStream();
        Stream target;
        IDisposable? compression = null;
        if (gzip)
        {
            compression = new GZipStream(output, CompressionLevel.SmallestSize, true);
            target = (Stream)compression;
        }
        else
        {
            target = output;
        }

        using (compression)
        using (var writer = new TarWriter(target, TarEntryFormat.Pax, true))
        {
            var directory = new PaxTarEntry(TarEntryType.Directory, "root/deep/");
            writer.WriteEntry(directory);

            var fileBytes = Encoding.UTF8.GetBytes("hello");
            var fileStream = new MemoryStream(fileBytes);
            var file = new PaxTarEntry(TarEntryType.RegularFile, "root\\deep\\įrašas.txt")
            {
                DataStream = fileStream,
                ModificationTime = new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            };

            writer.WriteEntry(file);
        }

        output.Position = 0;
        return output;
    }

    private static PluginCatalog CreateCatalog()
    {
        var plugin = new TarArchiveIndexPlugin();
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Plugin = plugin,
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }
}