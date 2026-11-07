using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class FileFormatCompatibilityTests
{
    [Theory]
    [InlineData("legacy-scd", "catalog-sample.scd", "catalog.scd")]
    [InlineData("legacy-cscd", "catalog-sample.cscd", "catalog.cscd")]
    [InlineData("legacy-ascd", "catalog-sample.ascd", "catalog.ascd")]
    [InlineData("skycd-json", "catalog-v1.json", "catalog.json")]
    [InlineData("skycd-csv", "catalog-hierarchy.csv", "catalog.csv")]
    [InlineData("skycd-xml", "catalog-v1.xml", "catalog.xml")]
    [InlineData("skycd-yaml", "catalog-v1.yaml", "catalog.yaml")]
    [InlineData("skycd-yaml", "catalog-v1.yaml", "catalog.yml")]
    [InlineData("skycd-toml", "catalog-v1.toml", "catalog.toml")]
    [InlineData("skycd-m3u", "catalog-playlist.m3u8", "catalog.m3u8")]
    [InlineData("skycd-m3u", "catalog-playlist.m3u8", "catalog.m3u")]
    [InlineData("skycd-pls", "catalog-playlist.pls", "catalog.pls")]
    [InlineData("skycd-xspf", "catalog-playlist.xspf", "catalog.xspf")]
    [InlineData("skycd-zip", "sample.zip", "catalog.zip")]
    [InlineData("skycd-tar", "sample.tar", "catalog.tar")]
    [InlineData("skycd-tar", "sample.tar.gz", "catalog.TAR.GZ")]
    [InlineData("skycd-tar", "sample.tar.gz", "catalog.tgz")]
    [InlineData("skycd-7z", "sample.7z", "catalog.7z")]
    [InlineData("skycd-iso", "catalog-sample.iso", "catalog.iso")]
    public async Task SupportedFormat_ReadsImportsAndRoundTripsWhenWritable(string format, string fixture, string fileName)
    {
        var manager = new FileFormatManager(CreateProviders());
        Assert.Equal(format, manager.GetInstanceFor(fileName).SupportedFormat.FormatId);
        Assert.Equal(format, manager.ResolveFormatId(null, fileName, forWrite: false));
        await using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "FormatFixtures", fixture));
        var result = await manager.ReadAsync(new FileFormatReadRequest { FormatId = string.Empty, Source = source, FileName = fileName });
        Assert.True(result.Success, result.Error);
        var entries = CatalogImportTests.Extract(result.Payload!);
        Assert.NotEmpty(entries);
        Assert.All(entries, entry => Assert.False(string.IsNullOrWhiteSpace(entry.Name)));
        Assert.All(entries.Where(entry => entry.ParentId is not null), entry => Assert.Contains(entries, parent => parent.Id == entry.ParentId));
        CatalogImportTests.Persist(entries);

        if (format is "skycd-csv" or "skycd-xml" or "skycd-yaml" or "skycd-toml")
        {
            var leaf = entries.Single(entry => entry.Type == CatalogDocumentType.File);
            Assert.NotNull(leaf.ParentId);
            Assert.True(leaf.Size > 0);
        }
        if (format is "skycd-zip" or "skycd-tar" or "skycd-7z" or "skycd-iso")
        {
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.Folder);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.File && entry.Size > 0);
        }

        if (!manager.GetInstanceFor(fileName).SupportedFormat.CanWrite) return;
        await using var target = new MemoryStream();
        await manager.WriteAsync(new FileFormatWriteRequest { FormatId = string.Empty, Target = target, FileName = fileName, Payload = result.Payload! });
        target.Position = 0;
        var reread = await manager.ReadAsync(new FileFormatReadRequest { FormatId = string.Empty, Source = target, FileName = fileName });
        var roundTrip = CatalogImportTests.Extract(reread.Payload!);
        Assert.Equal(entries.Select(entry => entry.Name), roundTrip.Select(entry => entry.Name));
        Assert.Equal(entries.Select(entry => entry.Type), roundTrip.Select(entry => entry.Type));
        Assert.Equal(entries.Select(entry => entry.Size), roundTrip.Select(entry => entry.Size));
    }

    [Theory]
    [InlineData("skycd-html", "catalog.html")]
    [InlineData("skycd-md", "catalog.md")]
    public async Task ExportOnlyFormats_StillExportAndRejectReading(string format, string name)
    {
        var manager = new FileFormatManager(CreateProviders());
        var payload = new List<Dictionary<string, object?>>
        {
            new() { ["nodeId"] = "1", ["parentId"] = "", ["kind"] = "folder", ["name"] = "Root", ["sizeBytes"] = "0" },
            new() { ["nodeId"] = "2", ["parentId"] = "1", ["kind"] = "file", ["name"] = "Track.mp3", ["sizeBytes"] = "42" }
        };
        Assert.Equal(format, manager.ResolveFormatId(null, name, forWrite: true));
        await using var target = new MemoryStream();
        var write = await manager.WriteAsync(new FileFormatWriteRequest { FormatId = string.Empty, Target = target, FileName = name, Payload = payload });
        Assert.True(write.Success, write.Error);
        var text = System.Text.Encoding.UTF8.GetString(target.ToArray());
        Assert.Contains("Root", text);
        Assert.Contains("Track.mp3", text);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => manager.ReadAsync(new FileFormatReadRequest { FormatId = string.Empty, Source = target, FileName = name }));
    }

    [Theory]
    [InlineData("ftpz.ascd", CatalogDocumentType.NetworkFolder, 10911)]
    [InlineData("my-documents.ascd", CatalogDocumentType.Folder, 3438)]
    public async Task HistoricalAscd_LoadsVisibleRootsAndBrowsableChildren(string file, CatalogDocumentType rootType, int count)
    {
        var service = new CatalogLoadService(new FileFormatManager(CreateProviders()));
        await service.LoadAsync(Path.Combine(AppContext.BaseDirectory, "FormatFixtures", file), (payload, _) =>
        {
            var entries = CatalogImportTests.Extract(payload!);
            Assert.Equal(count, entries.Count);
            var roots = entries.Where(entry => entry.ParentId is null).ToArray();
            Assert.NotEmpty(roots);
            Assert.All(roots, entry => Assert.Equal(rootType, entry.Type));
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.File);
            CatalogImportTests.Persist(entries, vm =>
            {
                Assert.Equal(roots.Select(entry => entry.Id).Order(), vm.TreeNodes.Select(node => node.Key).Order());
                Assert.NotEmpty(vm.TreeNodes.First().Children);
                Assert.NotEmpty(vm.BrowserItems);
                var child = vm.BrowserItems.First(entry => entry.Type == CatalogDocumentType.Folder);
                vm.SelectedBrowserItem = child;
                vm.NavigateToFolderCommand.Execute(null);
                Assert.Equal(child.Id, vm.SelectedTreeNode!.Key);
                Assert.NotEmpty(vm.BrowserItems);
            });
            return entries.Count;
        }, new Progress<int>());
    }

    private static IFileFormatPluginCapability[] CreateProviders() =>
    [
        new SkyCD.Plugin.Legacy.Scd.LegacyScdPlugin(),
        new SkyCD.Plugin.Legacy.Cscd.LegacyCscdPlugin(),
        new SkyCD.Plugin.Legacy.Ascd.LegacyAscdPlugin(),
        new SkyCD.Plugin.Json.JsonCatalogPlugin(),
        new SkyCD.Plugin.Csv.CsvCatalogPlugin(),
        new SkyCD.Plugin.Xml.XmlCatalogPlugin(),
        new SkyCD.Plugin.Yaml.YamlCatalogPlugin(),
        new SkyCD.Plugin.Toml.TomlCatalogPlugin(),
        new SkyCD.Plugin.M3u.M3uCatalogPlugin(),
        new SkyCD.Plugin.Pls.PlsCatalogPlugin(),
        new SkyCD.Plugin.Xspf.XspfCatalogPlugin(),
        new SkyCD.Plugin.Zip.ZipArchiveIndexPlugin(),
        new SkyCD.Plugin.Tar.TarArchiveIndexPlugin(),
        new SkyCD.Plugin.SevenZip.SevenZipArchiveIndexPlugin(),
        new SkyCD.Plugin.Iso.IsoImageIndexPlugin(),
        new SkyCD.Plugin.Html.HtmlCatalogExportPlugin(),
        new SkyCD.Plugin.Markdown.MarkdownCatalogExportPlugin()
    ];
}
