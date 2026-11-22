using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.Csv;
using SkyCD.Plugin.FileFormat.Html;
using SkyCD.Plugin.FileFormat.Json;
using SkyCD.Plugin.FileFormat.LegacySkyCD;
using SkyCD.Plugin.FileFormat.Markdown;
using SkyCD.Plugin.FileFormat.PlainText;
using SkyCD.Plugin.FileFormat.Playlist.M3u;
using SkyCD.Plugin.FileFormat.Playlist.Pls;
using SkyCD.Plugin.FileFormat.Playlist.Xspf;
using SkyCD.Plugin.FileFormat.Toml;
using SkyCD.Plugin.FileFormat.Xml;
using SkyCD.Plugin.FileFormat.XmlExport;
using SkyCD.Plugin.FileFormat.Yaml;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class SaveDataLossTests
{
    private const string FileId = "file-1";
    private const string ParentId = "root-1";
    private const string SizeMarker = "12345";
    private const string StatusMarker = "status-marker";
    private const string PropertyKey = "custom-property";
    private const string PropertyMarker = "property-marker";

    [Theory]
    [InlineData("skycd-json", ".json", "", true)]
    [InlineData("legacy-scd", ".scd", "id|parentId|status|property:custom-property|property:Path", false)]
    [InlineData("legacy-cscd", ".cscd", "id|parentId|status|property:custom-property|property:Path", false)]
    [InlineData("legacy-ascd", ".ascd", "status", false)]
    [InlineData("skycd-csv", ".csv", "status|property:custom-property|property:Path", false)]
    [InlineData("skycd-xml", ".xml", "status|property:custom-property|property:Path", false)]
    [InlineData("skycd-yaml", ".yaml", "status|property:custom-property|property:Path", false)]
    [InlineData("skycd-toml", ".toml", "status|property:custom-property|property:Path", false)]
    [InlineData("skycd-m3u", ".m3u",
        "id|parentId|kind|size|status|property:custom-property|property:Path", false)]
    [InlineData("skycd-pls", ".pls", "id|parentId|kind|status|property:custom-property|property:Path", false)]
    [InlineData("skycd-xspf", ".xspf",
        "id|parentId|kind|size|status|property:custom-property|property:Path", false)]
    [InlineData("skycd-html", ".html", "status|property:custom-property|property:Path", false)]
    [InlineData("skycd-md", ".md", "status|property:custom-property|property:Path", false)]
    [InlineData("skycd-txt-list", ".txt",
        "id|parentId|status|property:custom-property|property:Path", false)]
    [InlineData("skycd-xml-catalog", ".xml", "property:custom-property|property:Path", false)]
    [InlineData("skycd-xml-list", ".xml", "id|parentId|property:custom-property|property:Path", false)]
    public async Task DeclaredLoss_MatchesWhatTheWriterActuallyDrops(string formatId, string extension,
        string expectedLostText, bool preservesCatalogScope)
    {
        var manager = new FileFormatManager(CreateProviders());
        var entries = BuildEntries();
        var scope = CatalogScope();
        var rows = BuildRows();

        var loss = manager.AssessDataLoss(formatId, "catalog" + extension, entries, scope);
        var lost = expectedLostText.Length == 0
            ? Array.Empty<string>()
            : expectedLostText.Split('|', StringSplitOptions.RemoveEmptyEntries);
        var declared = loss
            .Where(item => item.Category == FileFormatDataLossCategories.Entries)
            .Select(item => item.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(lost.OrderBy(name => name, StringComparer.Ordinal), declared);

        if (preservesCatalogScope)
        {
            Assert.DoesNotContain(loss, item => item.Category == FileFormatDataLossCategories.Collections);
        }
        else
        {
            Assert.Contains(loss, item => item.Category == FileFormatDataLossCategories.Collections);
            Assert.Contains(loss, item => item.Category == FileFormatDataLossCategories.Loans);
        }

        var path = Path.Combine(Path.GetTempPath(), $"skycd-loss-{Guid.NewGuid():N}{extension}");
        try
        {
            await manager.WriteFileAsync(formatId, path, rows, catalogScopeItems: scope);
            var text = await ReadOutputAsync(formatId, path);
            Assert.Contains("Track.mp3", text);
            Assert.Contains("Root Folder", text);
            AssertField(text, FileId, !lost.Contains(FileFormatDataLossFields.Id));
            AssertField(text, ParentId, !lost.Contains(FileFormatDataLossFields.ParentId));
            var sizeMarker = formatId is "legacy-scd" or "legacy-cscd" ? "12.0556640" : SizeMarker;
            AssertField(text, sizeMarker, !lost.Contains(FileFormatDataLossFields.Size));
            AssertField(text, StatusMarker, !lost.Contains(FileFormatDataLossFields.Status));
            AssertField(text, PropertyKey, !lost.Contains(FileFormatDataLossFields.PropertyKeyPrefix + PropertyKey));
            AssertField(text, PropertyMarker,
                !lost.Contains(FileFormatDataLossFields.PropertyKeyPrefix + PropertyKey));
            if (preservesCatalogScope)
            {
                Assert.Contains("catalogScope", text);
            }
            else
            {
                Assert.DoesNotContain("Wishlist", text);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LossLines_LocalizeEveryCategoryFieldAndLabel()
    {
        var keys = new[]
        {
            UiKeys.SaveDataLoss, UiKeys.SaveDataLossIntro, UiKeys.SaveDataLossSaveAnyway,
            UiKeys.SaveDataLossDoNotSave, UiKeys.DataLossItemLine, UiKeys.DataLossItemLineWithSample,
            UiKeys.DataLossCategoryEntries, UiKeys.DataLossCategoryCollections, UiKeys.DataLossCategoryStatuses,
            UiKeys.DataLossCategoryLoans, UiKeys.DataLossCategoryBorrowers, UiKeys.DataLossCategoryScanExclusions,
            UiKeys.DataLossCategoryAiScanExclusions, UiKeys.DataLossFieldId, UiKeys.DataLossFieldParentId,
            UiKeys.DataLossFieldKind, UiKeys.DataLossFieldSize, UiKeys.DataLossFieldStatus,
            UiKeys.DataLossFieldProperties, UiKeys.DataLossFieldProperty, UiKeys.DataLossVirtualCollections,
            UiKeys.DataLossStatusDefinitions, UiKeys.DataLossScanRules, UiKeys.DataLossAiExclusions,
            UiKeys.DataLossActiveLoans, UiKeys.DataLossContacts
        };
        Assert.All(keys, key => Assert.NotEqual(key, LocalizationService.Current.Get(key)));

        var lines = CatalogDataLossText.BuildLines(
        [
            new(FileFormatDataLossCategories.Entries, "property:custom-property", 2, "One, Two"),
            new(FileFormatDataLossCategories.Entries, FileFormatDataLossFields.Size, 1, "Track.mp3"),
            new(FileFormatDataLossCategories.Collections, "virtualCollections", 3, "Favorites"),
            new(FileFormatDataLossCategories.Statuses, "statusDefinitions", 1),
            new(FileFormatDataLossCategories.Loans, "activeLoans", 1, "Alice"),
            new(FileFormatDataLossCategories.Borrowers, "contacts", 2),
            new(FileFormatDataLossCategories.ScanExclusions, "customRules", 1, "*.tmp"),
            new(FileFormatDataLossCategories.AiScanExclusions, "customMimeTypes", 1, "image/png")
        ]);

        Assert.Equal(8, lines.Count);
        Assert.Contains(lines, line => line.Contains("custom-property", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("One, Two", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.DoesNotContain('_', line));
    }

    [Fact]
    public async Task DecliningConfirmation_DoesNotCreateOrTouchTheTarget()
    {
        var service = new CatalogSaveService(new FileFormatManager(CreateProviders()));
        var target = Path.Combine(Path.GetTempPath(), $"skycd-decline-{Guid.NewGuid():N}.txt");
        try
        {
            var confirmed = false;
            var saved = await service.SaveAsync(target, BuildDocuments(), [], preview =>
            {
                confirmed = true;
                Assert.Equal("Catalog List Text Export (*.txt)", preview.FormatDisplayName);
                Assert.NotEmpty(preview.Items);
                return Task.FromResult(false);
            });
            Assert.True(confirmed);
            Assert.False(saved);
            Assert.False(File.Exists(target));

            await File.WriteAllTextAsync(target, "original");
            saved = await service.SaveAsync(target, BuildDocuments(), [], _ => Task.FromResult(false));
            Assert.False(saved);
            Assert.Equal("original", await File.ReadAllTextAsync(target));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task EmptyCatalogAndLosslessFormat_SaveWithoutConfirmation()
    {
        var service = new CatalogSaveService(new FileFormatManager(CreateProviders()));
        var target = Path.Combine(Path.GetTempPath(), $"skycd-lossless-{Guid.NewGuid():N}.json");
        try
        {
            var confirmed = false;
            var saved = await service.SaveAsync(target, [], [], _ =>
            {
                confirmed = true;
                return Task.FromResult(false);
            });
            Assert.True(saved);
            Assert.False(confirmed);
            Assert.True(File.Exists(target));
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public async Task AcceptingConfirmation_WritesTheAssessedSnapshotAndScope()
    {
        var service = new CatalogSaveService(new FileFormatManager(CreateProviders()));
        var target = Path.Combine(Path.GetTempPath(), $"skycd-accept-{Guid.NewGuid():N}.txt");
        try
        {
            var scope = CatalogScope();
            var preview = default(CatalogSavePreview);
            var saved = await service.SaveAsync(target, BuildDocuments(), scope, assessed =>
            {
                preview = assessed;
                return Task.FromResult(true);
            });
            Assert.True(saved);
            Assert.NotNull(preview);
            Assert.Contains(preview.Items, item => item.Category == FileFormatDataLossCategories.Loans);
            Assert.Contains(preview.Items, item => item.Category == FileFormatDataLossCategories.Entries);
            var text = await File.ReadAllTextAsync(target);
            Assert.Contains("Track.mp3", text);
            Assert.DoesNotContain(StatusMarker, text);
        }
        finally
        {
            File.Delete(target);
        }
    }

    [Fact]
    public void PluginWithoutAssessmentContract_IsTreatedAsLosslessButStillReportsScope()
    {
        var manager = new FileFormatManager([new PlainThirdPartyPlugin()]);
        var scope = CatalogScope();
        var loss = manager.AssessDataLoss("third-party", "catalog.bin", BuildEntries(), scope);
        Assert.Equal(scope, loss);
    }

    [Fact]
    public void ScopePreservingPlugin_DropsHostScopeItemsFromTheAssessment()
    {
        var manager = new FileFormatManager([new ScopeAwarePlugin()]);
        var loss = manager.AssessDataLoss("scope-aware", "catalog.bin", BuildEntries(), CatalogScope());
        Assert.Empty(loss);
    }

    private static void AssertField(string text, string marker, bool expected) =>
        Assert.Equal(expected, text.Contains(marker, StringComparison.Ordinal));

    private static async Task<string> ReadOutputAsync(string formatId, string path)
    {
        if (formatId is "legacy-cscd" or "legacy-ascd")
        {
            await using var file = File.OpenRead(path);
            using var deflate = new DeflateStream(file, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate);
            return await reader.ReadToEndAsync();
        }

        return await File.ReadAllTextAsync(path);
    }

    private static IReadOnlyList<FileFormatDataLossItem> CatalogScope() =>
    [
        new(FileFormatDataLossCategories.Collections, "virtualCollections", 2, "Favorites, Wishlist"),
        new(FileFormatDataLossCategories.Loans, "activeLoans", 1, "Alice")
    ];

    private static List<CatalogDocument> BuildDocuments() =>
    [
        new()
        {
            Id = ParentId,
            Name = "Root Folder",
            Type = CatalogDocumentType.Folder,
            ChildrenCount = 1,
            Properties = new PropertiesCollection { ["Path"] = @"C:\Music" }
        },
        new()
        {
            Id = FileId,
            ParentId = ParentId,
            Name = "Track.mp3",
            Type = CatalogDocumentType.File,
            Size = 12345,
            Status = StatusMarker,
            Properties = new PropertiesCollection
            {
                ["Path"] = @"C:\Music\Track.mp3",
                [PropertyKey] = PropertyMarker
            }
        }
    ];

    private static List<FileFormatDataLossEntry> BuildEntries() =>
        BuildDocuments().Select(document =>
        {
            var properties = document.Properties.ToDictionary(
                pair => pair.Key,
                pair => pair.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);
            return new FileFormatDataLossEntry(document.Id, document.ParentId, document.Name,
                document.Type.ToString(), document.Size, document.Status, properties);
        }).ToList();

    private static List<Dictionary<string, object?>> BuildRows() =>
        BuildDocuments().Select(document =>
        {
            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in document.Properties)
            {
                properties[key] = value;
            }

            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = document.Id,
                ["nodeId"] = document.Id,
                ["parentId"] = document.ParentId,
                ["name"] = document.Name,
                ["type"] = document.Type.ToString(),
                ["kind"] = document.Type.ToString().ToLowerInvariant(),
                ["size"] = document.Size,
                ["sizeBytes"] = document.Size,
                ["childrenCount"] = document.ChildrenCount,
                ["contentsState"] = document.ContentsState.ToString(),
                ["status"] = document.Status,
                ["properties"] = properties,
                ["path"] = document.Properties.TryGetValue("Path", out var path) ? path : null
            };
        }).ToList();

    private static IFileFormatPluginCapability[] CreateProviders() =>
    [
        new LegacyScdPlugin(),
        new LegacyCscdPlugin(),
        new LegacyAscdPlugin(),
        new JsonCatalogPlugin(),
        new CsvCatalogPlugin(),
        new XmlCatalogPlugin(),
        new YamlCatalogPlugin(),
        new TomlCatalogPlugin(),
        new M3uCatalogPlugin(),
        new PlsCatalogPlugin(),
        new XspfCatalogPlugin(),
        new HtmlCatalogExportPlugin(),
        new MarkdownCatalogExportPlugin(),
        new PlainTextListExportPlugin(),
        new XmlCatalogExportPlugin(),
        new XmlListExportPlugin()
    ];

    private sealed class PlainThirdPartyPlugin : IFileFormatPluginCapability
    {
        public FileFormatDescriptor SupportedFormat =>
            new("third-party", "Third party", [".bin"], [], false, true);

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatReadResult { Success = false });

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatWriteResult { Success = true });
    }

    private sealed class ScopeAwarePlugin : IFileFormatPluginCapability, IDataLossAwareFileFormatCapability
    {
        public FileFormatDescriptor SupportedFormat =>
            new("scope-aware", "Scope aware", [".bin"], [], false, true);

        public bool PreservesCatalogScope => true;

        public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
            FileFormatDataLossAssessment.Lossless;

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatReadResult { Success = false });

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileFormatWriteResult { Success = true });
    }
}
