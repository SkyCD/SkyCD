using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SkyCD.App;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Plugin.Runtime.Managers;
using Xunit;

namespace SkyCD.Plugin.DiskBase.Tests;

public sealed class DiskBaseIntegrationTests
{
    [Fact]
    public async Task DiscoveredPlugin_OpensAndPreservesLiveHierarchyWithoutModifyingSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-diskbase-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "catalog.qdr");
            await File.WriteAllBytesAsync(path, DiskBaseFixture.Create().Bytes);
            var originalHash = SHA256.HashData(await File.ReadAllBytesAsync(path));
            var discovered = new DiscoveredPluginFactory().BuildFromAssembly(typeof(DiskBasePlugin).Assembly);
            Assert.Equal("skycd.diskbase", discovered.Id);
            var plugin = Assert.Single(discovered.Capabilities.OfType<DiskBasePlugin>());
            var manager = new FileFormatManager([plugin]);
            Assert.Contains(manager.GetOpenFormats(), format => format.FormatId == "diskbase-qdr" && format.CanRead && !format.CanWrite);
            Assert.DoesNotContain(manager.GetSaveFormats(), format => format.FormatId == "diskbase-qdr");
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(typeof(DiskBasePlugin).Assembly.Location)!, "plugin.json")));

            using (var source = File.OpenRead(path))
            {
                source.Position = 7;
                Assert.True(plugin.CanHandleContent(source, path));
                Assert.Equal(7, source.Position);
                Assert.IsType<DiskBasePlugin>(manager.GetInstanceFor(path, source));
            }

            var documents = await new CatalogLoadService(manager).LoadAsync(path, (payload, _) => Extract(payload!), new Progress<int>());
            Assert.Equal(2, documents.Count(document => document.Type == CatalogDocumentType.Media));
            var media = Assert.Single(documents, document => document.Name == "café");
            Assert.Equal("VOL", media.Properties["DiskBaseVolumeLabel"]);
            Assert.Equal("C:\\source", media.Properties["DiskBaseSourcePath"]);
            Assert.Equal("4398046510080", media.Properties["DiskBaseCapacityBytes"]);
            Assert.Equal("12345678901", media.Properties["DiskBasePhysicalBytes"]);
            Assert.Equal("2021-02-03T04:05:06", media.Properties["DiskBaseScanDate"]);
            Assert.Equal("2021-02-03T04:05:06", media.Properties["DateModified"]);
            Assert.Equal("16", media.Properties["DiskBaseAttributes"]);
            var folderA = Assert.Single(documents, document => document.Name == "A");
            Assert.Equal("résumé", folderA.Properties["Description"]);
            var empty = Assert.Single(documents, document => document.Name == "Empty");
            Assert.Equal(folderA.Id, empty.ParentId);
            Assert.Equal(CatalogDocumentType.Folder, empty.Type);
            var duplicates = documents.Where(document => document.Name == "same.txt").ToArray();
            Assert.Equal(2, duplicates.Length);
            Assert.NotEqual(duplicates[0].ParentId, duplicates[1].ParentId);
            var archive = Assert.Single(documents, document => document.Name == "pack.zip");
            Assert.Equal(true, archive.Properties["DiskBaseArchive"]);
            Assert.Equal(archive.Id, Assert.Single(documents, document => document.Name == "inside.bin").ParentId);
            var rootFile = Assert.Single(documents, document => document.Name == "note.txt");
            Assert.Equal(media.Id, rootFile.ParentId);
            Assert.Equal(4294967295L, rootFile.Size);
            Assert.Equal("résumé", rootFile.Properties["Description"]);
            Assert.Equal("2021-02-03T04:05:06", rootFile.Properties["DateModified"]);
            Assert.DoesNotContain(documents, document => document.Name == "STALE");
            Assert.Equal(originalHash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
            Assert.Single(Directory.GetFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static IReadOnlyList<CatalogDocument> Extract(object payload)
    {
        var method = typeof(MainWindow).GetMethod("ExtractCatalogEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }
}
