using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Iso;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class IsoImageIndexPluginTests
{
    [Fact]
    public void OpenFormats_IncludeIso_ButSaveFormatsDoNot()
    {
        var service = CreateService(new FakeReader([]));

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-iso" && format.Extensions.Contains(".iso"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-iso");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyIsoFormat()
    {
        var service = CreateService(new FakeReader([]));
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
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
        var service = new FileFormatManager([new IsoImageIndexPlugin()]);
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Iso", "catalog-sample.iso");
        await using var source = File.OpenRead(fixturePath);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-iso",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP"));
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
        Assert.Contains(rows, row => Equals(row["sizeBytes"], "19"));
    }

    private static FileFormatManager CreateService(IIsoEntryReader reader)
    {
        return new FileFormatManager([new IsoImageIndexPlugin(reader)]);
    }

    private sealed class FakeReader(IReadOnlyCollection<IsoEntryInfo> entries) : IIsoEntryReader
    {
        public IReadOnlyCollection<IsoEntryInfo> ReadEntries(Stream source) => entries;
    }
}
