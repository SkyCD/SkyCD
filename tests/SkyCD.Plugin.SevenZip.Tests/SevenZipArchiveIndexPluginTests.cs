using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.SevenZip;
using Xunit;

namespace SkyCD.Plugin.Host.Tests;

public class SevenZipArchiveIndexPluginTests
{
    [Fact]
    public void OpenFormats_Include7z_ButSaveFormatsDoNot()
    {
        var service = CreateService();

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-7z" && format.Extensions.Contains(".7z"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-7z");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnly7zFormat()
    {
        var service = CreateService();
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
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
        var service = CreateService();
        var sevenZipPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "7z", "sample.7z");
        await using var source = new FileStream(sevenZipPath, FileMode.Open);

        var result = await service.ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-7z",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "root/deep/įrašas.txt"));
        Assert.Contains(rows, row => Equals(row["kind"], "file") && int.Parse(row["sizeBytes"].ToString()) > 0);
    }

    [Fact]
    public async Task ReadAsync_ReturnsTypedError_WhenCompressionMethodUnsupported()
    {
        var throwingReader = new ThrowingReader();
        var service = new FileFormatManager([new SevenZipArchiveIndexPlugin(throwingReader)]);
        await using var source = new MemoryStream([0x37, 0x7A]);

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.ReadAsync(new FileFormatReadRequest
            {
                FormatId = "skycd-7z",
                Source = source
            }));

        Assert.Contains("SEVENZIP_UNSUPPORTED_METHOD", exception.Message);
    }

    private static FileFormatManager CreateService()
    {
        return new FileFormatManager([new SevenZipArchiveIndexPlugin()]);
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
