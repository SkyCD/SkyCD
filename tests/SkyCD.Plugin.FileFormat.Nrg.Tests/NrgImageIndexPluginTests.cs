using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.FileFormat.Nrg.Tests;

public sealed class NrgImageIndexPluginTests
{
    private const string FixtureRelativePath = "Fixtures/Nrg/catalog-sample.nrg";

    [Fact]
    public void OpenFormats_IncludeNrg_ButSaveFormatsDoNot()
    {
        var service = new FileFormatManager([new NrgImageIndexPlugin()]);

        var openFormats = service.GetOpenFormats();
        var saveFormats = service.GetSaveFormats();

        Assert.Contains(openFormats, format => format.FormatId == "skycd-nrg" && format.Extensions.Contains(".nrg"));
        Assert.DoesNotContain(saveFormats, format => format.FormatId == "skycd-nrg");
    }

    [Fact]
    public async Task WriteAsync_IsBlocked_ForReadOnlyNrgFormat()
    {
        var service = new FileFormatManager([new NrgImageIndexPlugin()]);
        await using var target = new MemoryStream();

        var exception = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            service.WriteAsync(new FileFormatWriteRequest
            {
                FormatId = "skycd-nrg",
                Target = target,
                Payload = new { }
            }));

        Assert.Contains("read-only", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadAsync_ProjectsDirectoryTree_AndFileMetadata()
    {
        var rows = await ReadFixtureRowsAsync();

        Assert.Contains(rows, row => Equals(row["kind"], "folder") && Equals(row["fullPath"], "ROOT/DEEP"));
        var file = Assert.Single(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
        Assert.Equal("file", file["kind"]);
        Assert.Equal("FIXTURE.TXT", file["name"]);
        Assert.Equal("19", file["sizeBytes"]);
        Assert.NotNull(file["modifiedUtc"]);
    }

    [Theory]
    [InlineData(2336, 0x0300)]
    [InlineData(2352, 0x0500)]
    [InlineData(2352, 0x0600)]
    public async Task ReadAsync_RawSectorTrack_ExposesIsoFilesystem(int sectorSize, ushort mode)
    {
        var image = NrgImageBuilder.Create(NrgImageBuilder.BuildIso(), sectorSize, mode);
        await using var source = new MemoryStream(image);

        var result = await new NrgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-nrg",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
        Assert.Contains(rows, row => Equals(row["sizeBytes"], "19"));
    }

    [Fact]
    public async Task ReadAsync_ResolvesDataTrackAfterTrackPrefix()
    {
        var image = NrgImageBuilder.Create(NrgImageBuilder.BuildIso(), trackPrefix: new byte[4096]);
        await using var source = new MemoryStream(image);

        var result = await new NrgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-nrg",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP"));
    }

    [Fact]
    public async Task ReadAsync_VersionOneImage_IsSupported()
    {
        var image = NrgImageBuilder.Create(NrgImageBuilder.BuildIso(), version: 1);
        await using var source = new MemoryStream(image);

        var result = await new NrgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-nrg",
            Source = source
        });

        Assert.True(result.Success);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
        Assert.Contains(rows, row => Equals(row["fullPath"], "ROOT/DEEP/FIXTURE.TXT"));
    }

    [Fact]
    public async Task ReadAsync_DamagedImage_ReturnsFailure()
    {
        await using var source = new MemoryStream(new byte[64]);

        var result = await new NrgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-nrg",
            Source = source
        });

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private static async Task<List<Dictionary<string, object?>>> ReadFixtureRowsAsync()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, FixtureRelativePath);
        await using var source = File.OpenRead(fixturePath);

        var result = await new NrgImageIndexPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "skycd-nrg",
            Source = source
        });

        Assert.True(result.Success);
        return Assert.IsType<List<Dictionary<string, object?>>>(result.Payload);
    }
}
