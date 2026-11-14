using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.Katalog.CsvIdx.Tests;

public sealed class KatalogParserTests
{
    [Fact]
    public async Task ParseParametersAsync_ReadsVersion()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "parameters.csv");
        var parameters = await KatalogCsvIdxParser.ParseParametersAsync(path);
        Assert.True(parameters.ContainsKey("collection:version"));
        Assert.Equal("3.0", parameters["collection:version"]);
    }

    [Fact]
    public async Task ParseDevicesAsync_UnescapesFreeTextFields()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "device.csv");
        var devices = await KatalogCsvIdxParser.ParseDevicesAsync(path);
        Assert.Equal(3, devices.Count);

        var dev3 = devices.Find(d => d.DeviceId == 3);
        Assert.NotNull(dev3);
        Assert.Contains("\t", dev3.Comment);
        Assert.Contains("\n", dev3.Comment);
    }

    [Fact]
    public async Task ParseStorageAsync_ReadsRecords()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "storage.csv");
        var storages = await KatalogCsvIdxParser.ParseStorageAsync(path);
        Assert.Equal(2, storages.Count);
        Assert.Equal("Seagate", storages[0].Brand);
        Assert.Equal("SanDisk", storages[1].Brand);
    }

    [Fact]
    public async Task ParseTagsAsync_ReadsTags()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "tags.csv");
        var tags = await KatalogCsvIdxParser.ParseTagsAsync(path);
        Assert.Equal(3, tags.Count);
        Assert.Contains(tags, t => t.Name == "important");
    }

    [Fact]
    public async Task ParseFoldersIdxAsync_ReadsFolders()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "Backup Disk 1.folders.idx");
        var folders = await KatalogCsvIdxParser.ParseFoldersIdxAsync(path);
        Assert.Equal(2, folders.Count);
        Assert.Contains("/mnt/backup1/Documents", folders);
    }

    [Fact]
    public async Task ParseIdxAsync_ReadsHeaderAndFiles()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "Backup Disk 1.idx");
        var catalog = await KatalogCsvIdxParser.ParseIdxAsync(path);
        Assert.Equal(2, catalog.CatalogId);
        Assert.Equal("3.0", catalog.AppVersion);
        Assert.Single(catalog.Files);

        var file = catalog.Files[0];
        Assert.Equal("project_plan.pdf", Path.GetFileName(file.FullPath));
        Assert.Equal("pdf", file.Extension);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", file.ChecksumSha256);
        Assert.Equal("Plan Title", file.AudioTitle);
    }

    [Fact]
    public async Task ParseIdxAsync_HandlesLegacyV26()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "KatalogCollection", "legacy-v26.idx");
        var catalog = await KatalogCsvIdxParser.ParseIdxAsync(path);
        Assert.Equal(10, catalog.CatalogId);
        Assert.Equal("2.6", catalog.AppVersion);
        Assert.Equal(2, catalog.Files.Count);
        Assert.Equal(300, catalog.Files[0].SizeBytes);
        Assert.Equal(200, catalog.Files[1].SizeBytes);
    }
}
