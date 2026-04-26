using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.SevenZip;

namespace SkyCD.Plugin.Host.Tests;

public class SevenZipArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_Include7z_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager(CreateCatalog(new FakeReader([])).GetCapabilities<IFileFormatPluginCapability>());

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-7z" && format.Extensions.Contains(".7z"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-7z");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnly7zFormat()
    {
        var service = new FileFormatManager(CreateCatalog(new FakeReader([])).GetCapabilities<IFileFormatPluginCapability>());
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-7z",
            Target = target,
            Payload = new { }
        }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ProjectsNestedPathsAndMetadata()
    {
        var reader = new FakeReader(
        [
            new SevenZipEntryInfo("root/deep/įrašas.txt", IsDirectory: false, SizeBytes: 123, ModifiedUtc: new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc)),
            new SevenZipEntryInfo("root/docs/", IsDirectory: true, SizeBytes: 0, ModifiedUtc: null)
        ]);
        var service = new FileFormatManager(CreateCatalog(reader).GetCapabilities<IFileFormatPluginCapability>());
        await using var source = new MemoryStream([0x37, 0x7A]); // test stream ignored by fake reader

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-7z",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/įrašas.txt"));
        Assert.Contains(rows, row => Equals(row["kind"], "file") && Equals(row["sizeBytes"], "123"));
    }

    [Fact]
    public async Task ReadAsync_ReturnsTypedError_WhenCompressionMethodUnsupported()
    {
        var service = new FileFormatManager(CreateCatalog(new ThrowingReader()).GetCapabilities<IFileFormatPluginCapability>());
        await using var source = new MemoryStream([0x37, 0x7A]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-7z",
            Source = source
        }));

        Assert.Contains("SEVENZIP_UNSUPPORTED_METHOD", exception.Message);
    }

    private static PluginManager CreateCatalog(ISevenZipEntryReader reader)
    {
        var plugin = new SevenZipArchiveIndexPlugin(reader);
        var catalog = PluginManagerTestFactory.Create();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.7z",
                Name = "SevenZipArchiveIndexPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }

    private sealed class FakeReader(IReadOnlyCollection<SevenZipEntryInfo> entries) : ISevenZipEntryReader
    {
        public IReadOnlyCollection<SevenZipEntryInfo> ReadEntries(Stream source) => entries;
    }

    private sealed class ThrowingReader : ISevenZipEntryReader
    {
        public IReadOnlyCollection<SevenZipEntryInfo> ReadEntries(Stream source)
        {
            throw new NotSupportedException("LZMA2 variant is not supported.");
        }
    }
}
