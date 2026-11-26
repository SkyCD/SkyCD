using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.TestSupport;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Import;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5ThumbnailTests
{
    [Theory]
    [InlineData(0x0A0B0C0Du, "png", "Thumbnails/0a/0b/0c/0d.png")]
    [InlineData(0x0A0B0C0Du, ".JPEG", "Thumbnails/0a/0b/0c/0d.JPEG")]
    [InlineData(0x01000000u, null, "Thumbnails/01/00/00/00")]
    [InlineData(0u, "tiff", "Thumbnails/00/00/00/00.tiff")]
    public void CreateRelativePath_UsesMostSignificantByteFirst(uint key, string? extension, string expected)
    {
        Assert.Equal(expected, DiskCatalogMakerV5ThumbnailResolver.CreateRelativePath(key, extension));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(".", null)]
    [InlineData("   ", null)]
    [InlineData("not-an-extension", null)]
    [InlineData("abcdefghijklmnopq", null)]
    [InlineData("jpg", ".jpg")]
    [InlineData(" .webp ", ".webp")]
    public void NormalizeExtension_AcceptsOnlyShortAlphanumericValues(string? input, string? expected)
    {
        Assert.Equal(expected, DiskCatalogMakerV5ThumbnailResolver.NormalizeExtension(input));
    }

    [Fact]
    public void TryResolve_ReportsExistenceWithoutThrowing()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;

        var expectedRelative = "Thumbnails/0a/0b/0c/0d.png";
        Assert.False(DiskCatalogMakerV5ThumbnailResolver.TryResolve(
            root, 0x0A0B0C0D, "png", out var relative, out var full));
        Assert.Equal(expectedRelative, relative);
        Assert.NotNull(full);

        workspace.CreateFile("Thumbnails/0a/0b/0c/0d.png", [1]);

        Assert.True(DiskCatalogMakerV5ThumbnailResolver.TryResolve(
            root, 0x0A0B0C0D, "png", out relative, out full));
        Assert.Equal(expectedRelative, relative);
        Assert.True(File.Exists(full));

        Assert.False(DiskCatalogMakerV5ThumbnailResolver.TryResolve(
            null, 0x0A0B0C0D, "png", out relative, out full));
        Assert.Null(full);
    }

    [Fact]
    public void Build_ThumbnailWithoutRecord_UsesPathWithoutExtension()
    {
        var document = BuildDocument(thumbnailKey: 0x01020304, extension: null, packageRoot: null);

        var properties = Properties(Find(document, "item:1:2"));
        Assert.Equal("Thumbnails/01/02/03/04", properties["Thumbnail"]);
        Assert.False(properties.ContainsKey("ThumbnailResolved"));
    }

    [Fact]
    public void Build_ThumbnailFilePresent_IsReportedResolved()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;
        workspace.CreateFile("Thumbnails/0a/0b/0c/0d.png", [1]);

        var document = BuildDocument(thumbnailKey: 0x0A0B0C0D, extension: "png", packageRoot: root);

        var properties = Properties(Find(document, "item:1:2"));
        Assert.Equal("Thumbnails/0a/0b/0c/0d.png", properties["Thumbnail"]);
        Assert.Equal("true", properties["ThumbnailResolved"]);
    }

    [Fact]
    public void Build_ThumbnailFileMissing_IsReportedUnresolved()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.Root;

        var document = BuildDocument(thumbnailKey: 0x0A0B0C0D, extension: "png", packageRoot: root);

        var properties = Properties(Find(document, "item:1:2"));
        Assert.Equal("false", properties["ThumbnailResolved"]);
    }

    [Fact]
    public void Parse_DuplicateThumbnailKey_KeepsFirstRecordWithWarning()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "pic.jpg", thumbnailKey: 0x0A0B0C0D);
        builder.AddThumbnail(0x0A0B0C0D, 2, "png");
        builder.AddThumbnail(0x0A0B0C0D, 3, "jpg");

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());

        var thumbnail = Assert.Single(catalog.Thumbnails);
        Assert.Equal(2u, thumbnail.ItemId);
        Assert.Equal(".png", thumbnail.Extension);
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("appears more than once", StringComparison.Ordinal));
    }

    private static DiskCatalogMakerV5ReportDocument BuildDocument(
        uint thumbnailKey,
        string? extension,
        string? packageRoot)
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "pic.jpg", thumbnailKey: thumbnailKey);
        if (extension is not null)
        {
            builder.AddThumbnail(thumbnailKey, 2, extension);
        }

        return DiskCatalogMakerV5HierarchyBuilder.Build(
            DiskCatalogMakerV5Parser.Parse(builder.Build()),
            packageRoot);
    }

    private static Dictionary<string, object?> Find(DiskCatalogMakerV5ReportDocument document, string nodeId)
    {
        return Assert.Single(document.Rows, row => (string?)row["nodeId"] == nodeId);
    }

    private static Dictionary<string, object?> Properties(Dictionary<string, object?> row)
    {
        return Assert.IsType<Dictionary<string, object?>>(row["properties"]);
    }
}
