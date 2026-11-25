using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Exceptions;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Import;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

/// <summary>
/// The committed fixtures are the compatibility oracle for the v5 binary layout.
/// Every fixture is synthetic and contains no vendor data.
/// </summary>
public sealed class DiskCatalogMakerV5CheckedInFixtureTests
{
    private readonly DiskCatalogMakerV5Plugin plugin = new();

    [Theory]
    [InlineData("empty.dcmf")]
    [InlineData("single-volume-unicode.dcmf")]
    [InlineData("multiple-volumes.dcmf")]
    [InlineData("aliases.dcmf")]
    [InlineData("unknown-table.dcmf")]
    public void CanHandleContent_RecognizesCommittedFixture(string fileName)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(FixturePath(fileName)));

        Assert.True(plugin.CanHandleContent(stream, fileName));
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void EmptyFixture_ParsesWithoutVolumesOrItems()
    {
        var catalog = DiskCatalogMakerV5Parser.Parse(File.ReadAllBytes(FixturePath("empty.dcmf")));

        Assert.Empty(catalog.Volumes);
        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.Warnings);
    }

    [Fact]
    public void SingleVolumeFixture_PreservesHierarchyNamesSizesAndMetadata()
    {
        var catalog = DiskCatalogMakerV5Parser.Parse(
            File.ReadAllBytes(FixturePath("single-volume-unicode.dcmf")));
        var volume = Assert.Single(catalog.Volumes);
        Assert.Equal("Archive Disk", volume.Name);
        Assert.Equal("/dev/disk4", volume.Source);
        Assert.Equal(DiskCatalogMakerV5VolumeKind.Disk, volume.Kind);
        Assert.Equal(DiskCatalogMakerV5FileSystem.HfsPlus, volume.FileSystem);
        Assert.Equal(500_000_000_000UL, volume.CapacityBytes);
        Assert.Equal(5, catalog.Items.Count);

        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);
        Assert.Equal(6, document.Count);
        Assert.Equal("media:1", Find(document, "item:1:10")["parentId"]);
        Assert.Equal("item:1:10", Find(document, "item:1:11")["parentId"]);
        Assert.Equal("item:1:11", Find(document, "item:1:12")["parentId"]);
        Assert.Equal("item:1:11", Find(document, "item:1:13")["parentId"]);
        Assert.Equal("item:1:10", Find(document, "item:1:14")["parentId"]);

        var zeroByte = Find(document, "item:1:12");
        Assert.Equal("0", zeroByte["sizeBytes"]);
        Assert.Equal("TEXT", Properties(zeroByte)["TypeCode"]);
        Assert.Equal("Empty placeholder", Properties(zeroByte)["Comments"]);
        Assert.Equal("0644", Properties(zeroByte)["PosixMode"]);

        var large = Find(document, "item:1:13");
        Assert.Equal("5000000000000", large["sizeBytes"]);
        Assert.Equal("Large archive", Properties(large)["Comments"]);
        Assert.Equal("Red", Properties(Find(document, "item:1:12"))["LabelName"]);
        Assert.Equal("Purple", Properties(large)["LabelName"]);

        var documentFile = Find(document, "item:1:14");
        Assert.Equal("résumé.pdf", documentFile["name"]);
        Assert.Equal("PDF ", Properties(documentFile)["TypeCode"]);
        Assert.Equal("prvw", Properties(documentFile)["Creator"]);
        Assert.Equal("Archive Disk/ドキュメント/résumé.pdf", Properties(documentFile)["CatalogPath"]);
    }

    [Fact]
    public void MultipleVolumesFixture_KeepsDuplicateNamesUnderDistinctRoots()
    {
        var document = DiskCatalogMakerV5HierarchyBuilder.Build(
            DiskCatalogMakerV5Parser.Parse(File.ReadAllBytes(FixturePath("multiple-volumes.dcmf"))));

        Assert.Equal(8, document.Count);
        Assert.Equal("Archive Disk", Find(document, "media:1")["name"]);
        Assert.Equal("Backup 2026", Find(document, "media:2")["name"]);

        var readmes = document.Rows
            .Where(row => (string?)row["name"] == "readme.txt")
            .ToList();
        Assert.Equal(2, readmes.Count);
        Assert.Equal(2, readmes.Select(row => row["parentId"]).Distinct().Count());
        Assert.Equal("Archive Disk/Documents/readme.txt", Properties(readmes.Single(
            row => (string?)row["parentId"] == "item:1:10"))["CatalogPath"]);
        Assert.Equal("Backup 2026/Photos/readme.txt", Properties(readmes.Single(
            row => (string?)row["parentId"] == "item:2:10"))["CatalogPath"]);
    }

    [Fact]
    public void AliasFixture_PreservesAliasReferencesAndExtraData()
    {
        var catalog = DiskCatalogMakerV5Parser.Parse(File.ReadAllBytes(FixturePath("aliases.dcmf")));
        var alias = Assert.Single(catalog.Aliases);
        Assert.Equal("/Volumes/Backup", alias.MountPoint);
        Assert.Equal(9u, alias.TargetVolumeId);
        Assert.Equal([9, 8, 7], alias.RecursiveExtraBytes);

        var row = Find(
            DiskCatalogMakerV5HierarchyBuilder.Build(catalog),
            "item:1:21");
        var properties = Properties(row);
        Assert.Equal("true", properties["Alias"]);
        Assert.Equal("Archive/Documents", properties["AliasTargetPath"]);
        Assert.Equal("nas.local", properties["AliasServer"]);
        Assert.Equal("Résumé", properties["AliasUnicodeName"]);
        Assert.Equal("3", properties["AliasExtraBytes"]);
    }

    [Fact]
    public void UnknownTableFixture_PreservesOpaqueBytesWithWarning()
    {
        var catalog = DiskCatalogMakerV5Parser.Parse(File.ReadAllBytes(FixturePath("unknown-table.dcmf")));

        var unknown = Assert.Single(catalog.UnknownTables);
        Assert.Equal(0x0100u, unknown.TableId);
        Assert.Equal(7u, unknown.HeaderValue);
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, unknown.Payload);
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("unknown table 0x100", StringComparison.Ordinal));
        Assert.Single(catalog.Items);
    }

    [Fact]
    public async Task PackageWithThumbnailsFixture_ImportsAndResolvesObject()
    {
        var package = FixturePath("package-with-thumbnails");
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            FileName = package,
            SourcePath = package
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerV5ReportDocument>(result.Payload);
        Assert.Equal(package, document.Summary.PackageRoot);
        Assert.Equal("写真アーカイブ", Assert.Single(document.Rows, row => row["parentId"] is null)["name"]);
        var file = Find(document, "item:1:3");
        Assert.Equal("Thumbnails/0a/0b/0c/0d.png", Properties(file)["Thumbnail"]);
        Assert.Equal("true", Properties(file)["ThumbnailResolved"]);
    }

    [Fact]
    public async Task PackageWithoutThumbnailsFixture_ImportsAndToleratesMissingObjects()
    {
        var package = FixturePath("package-without-thumbnails");
        var result = await plugin.ReadAsync(new FileFormatReadRequest
        {
            FormatId = plugin.SupportedFormat.FormatId,
            Source = Stream.Null,
            SourcePath = package
        });

        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<DiskCatalogMakerV5ReportDocument>(result.Payload);
        Assert.Equal("false", Properties(Find(document, "item:1:3"))["ThumbnailResolved"]);
    }

    [Theory]
    [InlineData("corrupt-truncated-header.dcmf", "shorter than")]
    [InlineData("corrupt-truncated-table.dcmf", "requests")]
    [InlineData("corrupt-invalid-count.dcmf", "exceeding the supported maximum")]
    [InlineData("corrupt-invalid-offset.dcmf", "requests")]
    [InlineData("corrupt-overlap.dcmf", "overlap")]
    public void CorruptFixtures_ProduceFormatSpecificException(string fileName, string expectedFragment)
    {
        var payload = File.ReadAllBytes(FixturePath(fileName));

        var exception = Assert.Throws<DiskCatalogMakerV5CorruptCatalogException>(
            () => DiskCatalogMakerV5Parser.Parse(payload, fileName));
        Assert.Contains(expectedFragment, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnsupportedVersionFixture_ReportsVersionSpecificException()
    {
        var payload = File.ReadAllBytes(FixturePath("unsupported-version.dcmf"));

        var exception = Assert.Throws<DiskCatalogMakerV5UnsupportedVersionException>(
            () => DiskCatalogMakerV5Parser.Parse(payload));
        Assert.Contains("only format version 5", exception.Message, StringComparison.Ordinal);
    }

    internal static string FixturePath(string relativePath)
    {
        return Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "DiskCatalogMakerV5",
            relativePath);
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
