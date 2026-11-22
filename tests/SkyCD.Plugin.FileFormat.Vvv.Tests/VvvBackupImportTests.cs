using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.App.Views;
using SkyCD.Couchbase.Mapping;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Host.Menu;
using SkyCD.Plugin.FileFormat.Vvv;
using SkyCD.Plugin.FileFormat.Vvv.Exceptions;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Localization;
using Xunit;
using SkyCD.Plugin.FileFormat.Vvv.Import;
using SkyCD.Plugin.FileFormat.Vvv.Models;
using SkyCD.Plugin.FileFormat.Vvv.Parsing;

namespace SkyCD.Plugin.FileFormat.Vvv.Tests;

public sealed class VvvBackupImportTests
{
    private const string GenuineFixture = "VVV-1.5-fixture-backup.vvvbk";

    private const string UnsupportedSchemaFixture = "VVV-fixture-schema-999.vvvbk";

    [Fact]
    public void SupportedFormatIsReadOnlyBackupExtension()
    {
        var plugin = new VvvBackupPlugin();
        Assert.Equal("vvv-backup", plugin.SupportedFormat.FormatId);
        Assert.Equal(new[] { ".vvvbk" }, plugin.SupportedFormat.Extensions);
        Assert.True(plugin.SupportedFormat.CanRead);
        Assert.False(plugin.SupportedFormat.CanWrite);
        Assert.False((object)plugin is IContentAwareFileFormatCapability);
    }

    [Fact]
    public async Task Read_HonorsCancellationBeforeReading()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var source = new MemoryStream([1, 2, 3]);
        var request = new FileFormatReadRequest { FormatId = "vvv-backup", Source = source };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new VvvBackupPlugin().ReadAsync(request, cancellation.Token));
    }

    [Fact]
    public void Build_MapsPhysicalHierarchyDescriptionsAndAudioMetadata()
    {
        var snapshot = new VvvBackupSnapshot { SchemaVersion = 23 };
        snapshot.Volumes.Add(new VvvBackupVolume(
            1,
            "Audio CD",
            "Volume description",
            @"C:\music",
            new DateTime(2026, 1, 2, 3, 4, 5),
            null));
        snapshot.Paths.Add(new VvvBackupPath(1, 1, "Album", null, null));
        snapshot.Paths.Add(new VvvBackupPath(2, 1, "Disc 1", 1, "Disc description"));
        snapshot.Files.Add(new VvvBackupFile(
            1,
            "track01.flac",
            123,
            "flac",
            new DateTime(2025, 12, 31, 23, 59, 58),
            2,
            null,
            "Track description"));
        snapshot.Files.Add(new VvvBackupFile(2, "Disc 1", 0, null, null, null, 2, null));
        snapshot.AudioByFileId[1] = new VvvBackupAudio(
            "Song title",
            "Artist",
            "Album name",
            1999,
            "Comment",
            7,
            "Rock",
            1411,
            44100,
            2,
            245);

        var document = VvvBackupHierarchyBuilder.Build(snapshot, @"C:\exports\backup.vvvbk");

        Assert.Equal(1, document.Summary.Volumes);
        Assert.Equal(2, document.Summary.Folders);
        Assert.Equal(1, document.Summary.Files);
        Assert.Equal(0, document.Summary.SkippedRows);
        Assert.Equal(document.Count, document.Select(row => (string)row["nodeId"]!).Distinct().Count());
        Assert.All(
            document.Where(row => row["parentId"] is not null),
            row => Assert.Contains(document, parent => parent["nodeId"] as string == (string)row["parentId"]!));

        var media = Assert.Single(document, row => (string)row["kind"]! == "media");
        Assert.Equal("Audio CD", media["name"]);
        var mediaProperties = Properties(media);
        Assert.Equal("Volume description", mediaProperties["Description"]);
        Assert.Equal(@"C:\music", mediaProperties["PhysicalPath"]);
        Assert.Equal("2026-01-02 03:04:05", mediaProperties["CatalogDate"]);
        Assert.Equal("23", mediaProperties["VvvSchemaVersion"]);
        Assert.Equal("backup.vvvbk", mediaProperties["VvvSource"]);

        var album = Assert.Single(document, row => (string)row["name"]! == "Album" && (string)row["kind"]! == "folder");
        var disc = Assert.Single(document, row => (string)row["name"]! == "Disc 1" && (string)row["kind"]! == "folder");
        Assert.Equal(album["nodeId"], disc["parentId"]);
        Assert.Equal("Disc description", Properties(disc)["Description"]);

        var file = Assert.Single(document, row => (string)row["kind"]! == "file");
        Assert.Equal("track01.flac", file["name"]);
        Assert.Equal(123L, file["sizeBytes"]);
        Assert.Equal(disc["nodeId"], file["parentId"]);
        var fileProperties = Properties(file);
        Assert.Equal("flac", fileProperties["Extension"]);
        Assert.Equal("Track description", fileProperties["Description"]);
        Assert.Equal("2025-12-31 23:59:58", fileProperties["LastModified"]);
        Assert.Equal(@"C:\music\Album\Disc 1\track01.flac", fileProperties["PhysicalPath"]);
        Assert.Equal("Song title", fileProperties["Title"]);
        Assert.Equal("Artist", fileProperties["Artist"]);
        Assert.Equal("Album name", fileProperties["Album"]);
        Assert.Equal("1999", fileProperties["Year"]);
        Assert.Equal("7", fileProperties["TrackNumber"]);
        Assert.Equal("Rock", fileProperties["Genre"]);
        Assert.Equal("1411", fileProperties["Bitrate"]);
        Assert.Equal("44100", fileProperties["SampleRate"]);
        Assert.Equal("2", fileProperties["Channels"]);
        Assert.Equal("245", fileProperties["AudioDuration"]);
        Assert.Equal("1", fileProperties["VvvFileId"]);
    }

    [Fact]
    public void Build_MapsVirtualFolderLinksFilesAndSecondaryPlacements()
    {
        var snapshot = new VvvBackupSnapshot { SchemaVersion = 23 };
        snapshot.Volumes.Add(new VvvBackupVolume(1, "Disc", null, @"D:\", null, null));
        snapshot.Paths.Add(new VvvBackupPath(1, 1, "Music", null, null));
        snapshot.Paths.Add(new VvvBackupPath(2, 1, "Disc 1", 1, null));
        snapshot.Files.Add(new VvvBackupFile(1, "song.mp3", 77, "mp3", null, 2, null, null));
        snapshot.Files.Add(new VvvBackupFile(2, "Disc 1", 0, null, null, null, 2, null));
        snapshot.VirtualPaths.Add(new VvvBackupVirtualPath(10, "My Mix", null, null));
        snapshot.VirtualPaths.Add(new VvvBackupVirtualPath(11, "Disc 1", null, 2));
        snapshot.VirtualPaths.Add(new VvvBackupVirtualPath(12, "Other Mix", null, null));
        snapshot.VirtualFiles.Add(new VvvBackupVirtualFile(2, 10, null, 11));
        snapshot.VirtualFiles.Add(new VvvBackupVirtualFile(1, 10, 1, null));
        snapshot.VirtualFiles.Add(new VvvBackupVirtualFile(2, 12, null, 11));

        var document = VvvBackupHierarchyBuilder.Build(snapshot, "virtual.vvvbk");

        Assert.Equal(4, document.Summary.VirtualFolders);
        Assert.Equal(1, document.Summary.VirtualFiles);
        Assert.Equal(document.Count, document.Select(row => (string)row["nodeId"]!).Distinct().Count());

        var root = Assert.Single(document, row => (string)row["name"]! == "Virtual Folders");
        var mix = Assert.Single(document, row => (string)row["name"]! == "My Mix");
        Assert.Equal(root["nodeId"], mix["parentId"]);
        var disc = Assert.Single(
            document,
            row => (string)row["name"]! == "Disc 1" && row["parentId"] as string == (string)mix["nodeId"]!);
        Assert.Equal(@"D:\Music\Disc 1", Properties(disc)["PhysicalPath"]);
        var other = Assert.Single(document, row => (string)row["name"]! == "Other Mix");
        Assert.Equal(root["nodeId"], other["parentId"]);
        var reference = Assert.Single(
            document,
            row => (string)row["name"]! == "Disc 1" && row["parentId"] as string == (string)other["nodeId"]!);
        Assert.Equal("true", Properties(reference)["VvvVirtualLink"]);

        var file = Assert.Single(document, row => (string)row["kind"]! == "file" && row["parentId"] as string == (string)mix["nodeId"]!);
        Assert.Equal("song.mp3", file["name"]);
        Assert.Equal(77L, file["sizeBytes"]);
        Assert.Equal(@"D:\Music\Disc 1\song.mp3", Properties(file)["PhysicalPath"]);
        Assert.Contains(document.Summary.Notes, note => note.Contains("more than one location", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_ProducesStableIdsAndBreaksParentCycles()
    {
        var snapshot = new VvvBackupSnapshot { SchemaVersion = 23 };
        snapshot.Volumes.Add(new VvvBackupVolume(1, "Volume", null, null, null, null));
        snapshot.Paths.Add(new VvvBackupPath(1, 1, "A", 2, null));
        snapshot.Paths.Add(new VvvBackupPath(2, 1, "B", 1, null));

        var first = VvvBackupHierarchyBuilder.Build(snapshot, "cycle.vvvbk");
        var second = VvvBackupHierarchyBuilder.Build(snapshot, "cycle.vvvbk");

        Assert.Equal(
            first.Select(row => (string)row["nodeId"]!),
            second.Select(row => (string)row["nodeId"]!));
        Assert.Equal(first.Count, first.Select(row => (string)row["nodeId"]!).Distinct().Count());
        Assert.All(
            first.Where(row => row["parentId"] is not null),
            row => Assert.Contains(first, parent => parent["nodeId"] as string == (string)row["parentId"]!));
        Assert.Contains(first.Summary.Notes, note => note.Contains("circular", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_HandlesExcessiveHierarchyDepthWithoutRecursionFailure()
    {
        var snapshot = new VvvBackupSnapshot { SchemaVersion = 23 };
        snapshot.Volumes.Add(new VvvBackupVolume(1, "Volume", null, null, null, null));
        for (var index = 1; index <= 300; index++)
        {
            snapshot.Paths.Add(new VvvBackupPath(
                index,
                1,
                $"Folder {index}",
                index == 300 ? null : index + 1,
                null));
        }

        var document = VvvBackupHierarchyBuilder.Build(snapshot, "deep.vvvbk");

        Assert.Equal(300, document.Summary.Folders);
        Assert.Equal(document.Count, document.Select(row => (string)row["nodeId"]!).Distinct().Count());
        Assert.Contains(document.Summary.Notes, note => note.Contains("depth limit", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_EmptySnapshotProducesNoNodes()
    {
        var document = VvvBackupHierarchyBuilder.Build(new VvvBackupSnapshot { SchemaVersion = 23 }, "empty.vvvbk");

        Assert.Empty(document);
        Assert.Equal(0, document.Summary.Volumes);
        Assert.Equal(0, document.Summary.Folders);
        Assert.Equal(0, document.Summary.Files);
        Assert.Equal(0, document.Summary.VirtualFolders);
        Assert.Equal(0, document.Summary.VirtualFiles);
    }

    [Fact]
    public async Task Read_GenuineBackupProducesVerifiedPhysicalHierarchy()
    {
        if (!EngineIsAvailable())
        {
            return;
        }

        var fixturePath = FixturePath(GenuineFixture);
        var hashBefore = Sha256(fixturePath);

        var result = await ReadAsync(GenuineFixture);

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<VvvBackupReportDocument>(result.Payload);
        Assert.Equal(23, document.Summary.SchemaVersion);
        Assert.Equal(1, document.Summary.Volumes);
        Assert.Equal(2, document.Summary.Folders);
        Assert.Equal(2, document.Summary.Files);
        Assert.Equal(0, document.Summary.VirtualFolders);
        Assert.Equal(0, document.Summary.VirtualFiles);
        Assert.Equal(0, document.Summary.SkippedRows);
        Assert.Equal(document.Count, document.Select(row => (string)row["nodeId"]!).Distinct().Count());

        var media = Assert.Single(document, row => (string)row["kind"]! == "media");
        Assert.Equal("Fixture volume", media["name"]);
        Assert.Equal("Fixture volume", Properties(media)["VvvVolume"]);
        Assert.Equal("23", Properties(media)["VvvSchemaVersion"]);

        var sample = Assert.Single(
            document,
            row => (string)row["name"]! == "sample" && (string)row["kind"]! == "folder");
        Assert.Equal(media["nodeId"], sample["parentId"]);
        var hello = Assert.Single(document, row => (string)row["name"]! == "hello.txt");
        Assert.Equal(39L, hello["sizeBytes"]);
        Assert.Equal(sample["nodeId"], hello["parentId"]);
        var folder = Assert.Single(document, row => (string)row["name"]! == "folder" && (string)row["kind"]! == "folder");
        Assert.Equal(sample["nodeId"], folder["parentId"]);
        var nested = Assert.Single(document, row => (string)row["name"]! == "nested.txt");
        Assert.Equal(22L, nested["sizeBytes"]);
        Assert.Equal(folder["nodeId"], nested["parentId"]);

        Assert.Equal(hashBefore, Sha256(fixturePath));
    }

    [Fact]
    public async Task Read_GenuineBackupPersistsHierarchyAcrossRepositoryRecreation()
    {
        if (!EngineIsAvailable())
        {
            return;
        }

        var result = await ReadAsync(GenuineFixture);
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<VvvBackupReportDocument>(result.Payload);
        var documents = ExtractCatalogEntries(document);

        Assert.Equal(5, documents.Count);
        Assert.Equal(CatalogDocumentType.Media, documents.Single(entry => entry.Name == "Fixture volume").Type);
        Assert.Equal(CatalogDocumentType.Folder, documents.Single(entry => entry.Name == "sample").Type);
        Assert.Equal(CatalogDocumentType.File, documents.Single(entry => entry.Name == "hello.txt").Type);

        PersistAndVerify(documents);
    }

    [Fact]
    public async Task Read_RejectsNonBackupData()
    {
        if (!EngineIsAvailable())
        {
            return;
        }

        var invalid = Path.Combine(Path.GetTempPath(), "skycd-vvvbk-invalid-" + Guid.NewGuid().ToString("N") + ".vvvbk");
        try
        {
            await File.WriteAllTextAsync(invalid, "this is not a Firebird backup");

            await using var stream = File.OpenRead(invalid);
            var result = await new VvvBackupPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "vvv-backup",
                Source = stream,
                FileName = Path.GetFileName(invalid)
            });

            Assert.False(result.Success);
            Assert.Contains("VVV backup", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(invalid))
            {
                File.Delete(invalid);
            }
        }
    }

    [Fact]
    public async Task Read_RejectsTruncatedBackup()
    {
        if (!EngineIsAvailable())
        {
            return;
        }

        var truncated = Path.Combine(Path.GetTempPath(), "skycd-vvvbk-truncated-" + Guid.NewGuid().ToString("N") + ".vvvbk");
        try
        {
            using (var source = File.OpenRead(FixturePath(GenuineFixture)))
            using (var target = File.Create(truncated))
            {
                var buffer = new byte[2048];
                var read = source.Read(buffer, 0, buffer.Length);
                target.Write(buffer, 0, read);
            }

            await using var stream = File.OpenRead(truncated);
            var result = await new VvvBackupPlugin().ReadAsync(new FileFormatReadRequest
            {
                FormatId = "vvv-backup",
                Source = stream,
                FileName = Path.GetFileName(truncated)
            });

            Assert.False(result.Success);
            Assert.Contains("truncated", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (File.Exists(truncated))
            {
                File.Delete(truncated);
            }
        }
    }

    [Fact]
    public async Task Read_RejectsUnsupportedSchemaVersion()
    {
        if (!EngineIsAvailable())
        {
            return;
        }

        var result = await ReadAsync(UnsupportedSchemaFixture);

        Assert.False(result.Success);
        Assert.Contains("schema version 999 is not supported", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EngineIsAvailable()
    {
        try
        {
            VvvBackupEngineLocator.Resolve();
            return true;
        }
        catch (VvvBackupEngineUnavailableException)
        {
            return false;
        }
    }

    private static async Task<FileFormatReadResult> ReadAsync(string fixture)
    {
        var path = FixturePath(fixture);
        await using var stream = File.OpenRead(path);
        return await new VvvBackupPlugin().ReadAsync(new FileFormatReadRequest
        {
            FormatId = "vvv-backup",
            Source = stream,
            FileName = fixture,
            SourcePath = path
        });
    }

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "VvvBackup", name);

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static Dictionary<string, object?> Properties(Dictionary<string, object?> row) =>
        Assert.IsType<Dictionary<string, object?>>(row["properties"]);

    private static IReadOnlyList<CatalogDocument> ExtractCatalogEntries(object payload)
    {
        var method = typeof(MainWindow).GetMethod(
            "ExtractCatalogEntries",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsAssignableFrom<IReadOnlyList<CatalogDocument>>(method.Invoke(null, [payload]));
    }

    private static void PersistAndVerify(IReadOnlyList<CatalogDocument> documents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "skycd-vvvbk-persist-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var database = new Database("catalog", new DatabaseConfiguration { Directory = directory }))
            {
                var collection = database.GetDefaultCollection();
                foreach (var entry in documents)
                {
                    using var document = entry.ToMutableDocument(entry.Id);
                    collection.Save(document);
                }
            }

            using (var database = new Database("catalog", new DatabaseConfiguration { Directory = directory }))
            {
                var collection = database.GetDefaultCollection();
                var repository = new CatalogDocumentRepository();
                repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
                var constructor = typeof(MainWindowViewModel)
                    .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Single(candidate => candidate.GetParameters().Length == 5);
                using var viewModel = Assert.IsType<MainWindowViewModel>(constructor.Invoke(
                    [
                        repository,
                        new PropertyValueLocalizer(),
                        new MenuExtensionManager(Array.Empty<IMenuPluginCapability>()),
                        false,
                        null
                    ]));

                var volume = Assert.Single(viewModel.TreeNodes);
                Assert.Equal("Fixture volume", volume.Title);
                var sample = Assert.Single(volume.Children);
                Assert.Equal("sample", sample.Title);
                var folder = Assert.Single(sample.Children);
                Assert.Equal("folder", folder.Title);

                viewModel.SelectedTreeNode = sample;
                Assert.Contains(viewModel.BrowserItems, item => item.Name == "hello.txt");
                Assert.Contains(viewModel.BrowserItems, item => item.Name == "folder");

                viewModel.SelectedTreeNode = folder;
                var nested = Assert.Single(viewModel.BrowserItems);
                Assert.Equal("nested.txt", nested.Name);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
