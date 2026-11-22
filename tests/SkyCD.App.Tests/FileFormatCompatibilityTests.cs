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
    [InlineData("skycd-nrg", "catalog-sample.nrg", "catalog.nrg")]
    [InlineData("skycd-qcow2", "catalog-sample.qcow2", "catalog.qcow2")]
    [InlineData("skycd-dmg", "catalog-sample.dmg", "catalog.dmg")]
    [InlineData("skycd-daa", "catalog-sample.daa", "catalog.daa")]
    [InlineData("skycd-vhd", "catalog-sample.vhd", "catalog.vhd")]
    [InlineData("skycd-vdi", "catalog.vdi", "catalog.vdi")]
    [InlineData("skycd-arj", "catalog.arj", "catalog.arj")]
    [InlineData("skycd-ace", "catalog.ace", "catalog.ace")]
    [InlineData("skycd-lha", "catalog.lha", "catalog.lha")]
    [InlineData("skycd-lha", "catalog.lha", "catalog.lzh")]
    [InlineData("skycd-gzip", "catalog.gz", "catalog.gz")]
    [InlineData("skycd-gzip", "catalog.gz", "catalog.gzip")]
    [InlineData("skycd-zstd", "catalog.zst", "catalog.zst")]
    [InlineData("skycd-zstd", "catalog.zst", "catalog.tzst")]
    [InlineData("skycd-bzip2", "catalog.bz2", "catalog.bz2")]
    [InlineData("skycd-bzip2", "catalog.bz2", "catalog.bz")]
    [InlineData("skycd-arc", "catalog.arc", "catalog.arc")]
    [InlineData("skycd-zoo", "catalog.zoo", "catalog.zoo")]
    [InlineData("skycd-xz", "catalog.xz", "catalog.xz")]
    [InlineData("skycd-xz", "catalog.xz", "catalog.txz")]
    [InlineData("skycd-cab", "catalog.cab", "catalog.cab")]
    [InlineData("skycd-rar", "catalog.rar", "catalog.rar")]
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
        if (format is "skycd-zip" or "skycd-tar" or "skycd-7z" or "skycd-iso" or "skycd-qcow2" or "skycd-dmg"
            or "skycd-daa" or "skycd-arj" or "skycd-vhd" or "skycd-vdi" or "skycd-lha" or "skycd-zoo"
            or "skycd-arc" or "skycd-cab" or "skycd-rar" or "skycd-ace")
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

    [Fact]
    public async Task MdfImage_ResolvesCompanionImageAndImportsCatalog()
    {
        var manager = new FileFormatManager(CreateProviders());
        var path = Path.Combine(AppContext.BaseDirectory, "FormatFixtures", "catalog-sample.mds");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.ChangeExtension(path, ".mdf")));
        Assert.Equal("skycd-mdf", manager.ResolveFormatId(null, path, forWrite: false));

        var service = new CatalogLoadService(manager);
        await service.LoadAsync(path, (payload, _) =>
        {
            var entries = CatalogImportTests.Extract(payload!);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.Folder);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.File && entry.Size > 0);
            CatalogImportTests.Persist(entries);
            return entries.Count;
        }, new Progress<int>());
    }

    [Fact]
    public async Task CloneCdImage_ResolvesCompanionImageAndImportsCatalog()
    {
        var manager = new FileFormatManager(CreateProviders());
        var path = Path.Combine(AppContext.BaseDirectory, "FormatFixtures", "catalog-sample.ccd");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.ChangeExtension(path, ".img")));
        Assert.Equal("skycd-ccd", manager.ResolveFormatId(null, path, forWrite: false));

        var service = new CatalogLoadService(manager);
        await service.LoadAsync(path, (payload, _) =>
        {
            var entries = CatalogImportTests.Extract(payload!);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.Folder);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.File && entry.Size > 0);
            CatalogImportTests.Persist(entries);
            return entries.Count;
        }, new Progress<int>());
    }

    [Fact]
    public async Task BinCueImage_ResolvesCompanionImageAndImportsCatalog()
    {
        var manager = new FileFormatManager(CreateProviders());
        var path = Path.Combine(AppContext.BaseDirectory, "FormatFixtures", "catalog-sample.cue");
        Assert.True(File.Exists(path));
        Assert.True(File.Exists(Path.ChangeExtension(path, ".bin")));
        Assert.Equal("skycd-bincue", manager.ResolveFormatId(null, path, forWrite: false));

        var service = new CatalogLoadService(manager);
        await service.LoadAsync(path, (payload, _) =>
        {
            var entries = CatalogImportTests.Extract(payload!);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.Folder);
            Assert.Contains(entries, entry => entry.Type == CatalogDocumentType.File && entry.Size > 0);
            CatalogImportTests.Persist(entries);
            return entries.Count;
        }, new Progress<int>());
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

    [Fact]
    public async Task WinCatalogCsv_ReadsAndImportsThroughFileFormatManager()
    {
        var manager = new FileFormatManager(CreateProviders());
        var fixture = "wincatalog-standard-en.csv";
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "FormatFixtures", fixture);
        await using var source = File.OpenRead(fixturePath);
        Assert.Equal("wincatalog-csv", manager.GetInstanceFor(fixture, source).SupportedFormat.FormatId);
        source.Position = 0;
        var result = await manager.ReadAsync(new FileFormatReadRequest { FormatId = string.Empty, Source = source, FileName = fixture });
        Assert.True(result.Success, result.Error);
        var entries = CatalogImportTests.Extract(result.Payload!);
        Assert.NotEmpty(entries);
        CatalogImportTests.Persist(entries);
    }

    private static IFileFormatPluginCapability[] CreateProviders() =>
    [
        new SkyCD.Plugin.FileFormat.Legacy.Scd.LegacyScdPlugin(),
        new SkyCD.Plugin.FileFormat.Legacy.Cscd.LegacyCscdPlugin(),
        new SkyCD.Plugin.FileFormat.Legacy.Ascd.LegacyAscdPlugin(),
        new SkyCD.Plugin.FileFormat.Json.JsonCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.Csv.CsvCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.WinCatalog.Csv.WinCatalogCsvPlugin(),
        new SkyCD.Plugin.FileFormat.Katalog.CsvIdx.KatalogCsvIdxPlugin(),
        new SkyCD.Plugin.FileFormat.Xml.XmlCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.Yaml.YamlCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.Toml.TomlCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.M3u.M3uCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.Pls.PlsCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.Xspf.XspfCatalogPlugin(),
        new SkyCD.Plugin.FileFormat.Zip.ZipArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Tar.TarArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.SevenZip.SevenZipArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Iso.IsoImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Nrg.NrgImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Qcow2.Qcow2ImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Dmg.DmgImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Daa.DaaImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Mdf.MdfImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Ccd.CcdImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.BinCue.BinCueImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Vhd.VhdImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Vdi.VdiImageIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Arj.ArjArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Ace.AceArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Lha.LhaArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Gzip.GzipArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Zstd.ZstdArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Bzip2.Bzip2ArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Arc.ArcArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Zoo.ZooArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.XZ.XzArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Cab.CabArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Rar.RarArchiveIndexPlugin(),
        new SkyCD.Plugin.FileFormat.Html.HtmlCatalogExportPlugin(),
        new SkyCD.Plugin.FileFormat.Markdown.MarkdownCatalogExportPlugin()
    ];
}
