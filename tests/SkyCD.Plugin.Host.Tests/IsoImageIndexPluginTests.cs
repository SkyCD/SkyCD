using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Iso;

namespace SkyCD.Plugin.Host.Tests;

public class IsoImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeIso_ButSaveFormatsDoNot()
    {
        var service = new FileFormatRoutingService(CreateCatalog(new FakeReader([])));

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-iso" && format.Extensions.Contains(".iso"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-iso");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyIsoFormat()
    {
        var service = new FileFormatRoutingService(CreateCatalog(new FakeReader([])));
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAsync<FileFormatRoutingException>(() => service.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = "skycd-iso",
            Target = target,
            Payload = new { }
        }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ProjectsDirectoryTree_AndLargeFileMetadata()
    {
        var reader = new FakeReader(
        [
            new IsoEntryInfo("ROOT", IsDirectory: true, SizeBytes: 0, ModifiedUtc: null),
            new IsoEntryInfo("ROOT/DEEP", IsDirectory: true, SizeBytes: 0, ModifiedUtc: null),
            new IsoEntryInfo("ROOT/DEEP/MOVIE.MKV", IsDirectory: false, SizeBytes: 8589934592L, ModifiedUtc: new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))
        ]);
        var service = new FileFormatRoutingService(CreateCatalog(reader));
        await using var source = new MemoryStream([0x43, 0x44]); // fake stream for fixture reader

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP/MOVIE.MKV"));
        Assert.Contains(rows, row => Equals(row["sizeBytes"], "8589934592"));
    }

    private static PluginCatalog CreateCatalog(IIsoEntryReader reader)
    {
        var plugin = new IsoImageIndexPlugin(reader);
        var catalog = new PluginCatalog();
        catalog.SetPlugins(
        [
            new DiscoveredPlugin
            {
                Id = "tests.iso",
                Name = "IsoImageIndexPluginTests",
                Version = new Version(1, 0, 0),
                MinHostVersion = new Version(3, 0, 0),
                FileName = "tests.dll",
                Capabilities = [plugin]
            }
        ]);
        return catalog;
    }

    private sealed class FakeReader(IReadOnlyCollection<IsoEntryInfo> entries) : IIsoEntryReader
    {
        public IReadOnlyCollection<IsoEntryInfo> ReadEntries(Stream source) => entries;
    }
}
