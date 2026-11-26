using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Plugin.FileFormat.Html;
using SkyCD.Plugin.FileFormat.Json;
using SkyCD.Plugin.FileFormat.Markdown;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.SDK.Capabilities.FileFormats;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.App.Tests;

public sealed class ExportFormatTests
{
    [Fact]
    public void GetExportFormats_ListsOnlyWriteOnlyExporters()
    {
        var manager = new FileFormatManager(
            [new JsonCatalogPlugin(), new HtmlCatalogExportPlugin(), new MarkdownCatalogExportPlugin()]);

        var exports = manager.GetExportFormats();

        Assert.Equal(["skycd-html", "skycd-md"],
            exports.Select(format => format.FormatId).OrderBy(id => id, StringComparer.Ordinal));
        Assert.DoesNotContain(exports, format => format.CanRead);
        Assert.All(exports, format => Assert.True(format.CanWrite));
    }

    [Fact]
    public void GetExportFiltersFor_LimitsThePickerToTheChosenFormat()
    {
        var manager = new FileFormatManager(
            [new JsonCatalogPlugin(), new HtmlCatalogExportPlugin(), new MarkdownCatalogExportPlugin()]);

        var filters = manager.GetExportFiltersFor("skycd-md");

        var single = Assert.Single(filters);
        Assert.Contains("*.md", single.Patterns ?? []);
        Assert.Empty(manager.GetExportFiltersFor("skycd-json"));
        Assert.Empty(manager.GetExportFiltersFor("unknown-format"));
    }

    [Fact]
    public async Task ExportingRunsTheSameWriteAsSaveAs_WithDataLossConfirmation()
    {
        var service = new CatalogSaveService(new FileFormatManager(
            [new JsonCatalogPlugin(), new HtmlCatalogExportPlugin(), new MarkdownCatalogExportPlugin()]));
        using var workspace = new TestWorkspace();
        var target = workspace.GetPath("export.html");
        var preview = default(CatalogSavePreview);
        var saved = await service.SaveAsync(target, BuildDocuments(), [], assessed =>
        {
            preview = assessed;
            return Task.FromResult(true);
        });

        Assert.True(saved);
        Assert.NotNull(preview);
        Assert.NotEmpty(preview.Items);
        var text = await File.ReadAllTextAsync(target);
        Assert.Contains("Track.mp3", text);
    }

    [Fact]
    public async Task DecliningTheLossConfirmation_LeavesTheExportTargetUntouched()
    {
        var service = new CatalogSaveService(new FileFormatManager(
            [new HtmlCatalogExportPlugin()]));
        using var workspace = new TestWorkspace();
        var target = workspace.GetPath("export.html");
        await File.WriteAllTextAsync(target, "original");
        var saved = await service.SaveAsync(target, BuildDocuments(), [],
            _ => Task.FromResult(false));

        Assert.False(saved);
        Assert.Equal("original", await File.ReadAllTextAsync(target));
    }

    private static List<CatalogDocument> BuildDocuments() =>
    [
        new()
        {
            Id = "root-1",
            Name = "Root Folder",
            Type = CatalogDocumentType.Folder,
            ChildrenCount = 1,
            Properties = new PropertiesCollection { ["Path"] = @"C:\Music" }
        },
        new()
        {
            Id = "file-1",
            ParentId = "root-1",
            Name = "Track.mp3",
            Type = CatalogDocumentType.File,
            Size = 12345,
            Properties = new PropertiesCollection { ["Path"] = @"C:\Music\Track.mp3" }
        }
    ];
}
