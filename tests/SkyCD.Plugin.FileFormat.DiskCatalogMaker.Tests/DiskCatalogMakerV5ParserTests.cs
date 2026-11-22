using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker;
using Xunit;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Import;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Models;
using SkyCD.Plugin.FileFormat.DiskCatalogMaker.Parsing;

namespace SkyCD.Plugin.FileFormat.DiskCatalogMaker.Tests;

public sealed class DiskCatalogMakerV5ParserTests
{
    private const long CreatedUnix = 1_700_000_000;
    private const long ModifiedUnix = 1_700_100_000;
    private const long ScannedUnix = 1_700_200_000;

    [Fact]
    public void Parse_EmptyCatalog_HasNoVolumesOrItems()
    {
        var catalog = DiskCatalogMakerV5Parser.Parse(new DiskCatalogMakerV5FixtureBuilder().Build());
        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);

        Assert.Empty(catalog.Volumes);
        Assert.Empty(catalog.Items);
        Assert.Empty(catalog.Warnings);
        Assert.Empty(document.Rows);
        Assert.Equal(0, document.Summary.VolumeCount);
        Assert.Equal(0, document.Summary.ItemCount);
        Assert.Equal(0, document.Summary.ImportedItemCount);
    }

    [Fact]
    public void Parse_UnicodeHierarchyAndItemProperties_AreMapped()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(
            7,
            "My Disk",
            kind: DiskCatalogMakerV5VolumeKind.Disk,
            fileSystem: DiskCatalogMakerV5FileSystem.HfsPlus,
            capacityBytes: 500_000_000_000,
            freeBytes: 100_000_000_000,
            createdUnix: CreatedUnix,
            scannedUnix: ScannedUnix,
            source: "/dev/disk4");
        builder.AddItem(7, 10, null, "ドキュメント", DiskCatalogMakerV5ItemKind.Directory, createdUnix: CreatedUnix);
        builder.AddItem(
            7,
            11,
            10,
            "résumé.pdf",
            DiskCatalogMakerV5ItemKind.File,
            sizeBytes: 12345,
            createdUnix: CreatedUnix,
            modifiedUnix: ModifiedUnix,
            typeCode: "PDF ",
            creatorCode: "prvw",
            comment: "Résumé final",
            label: 3,
            posixMode: Convert.ToInt32("755", 8));

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());
        var volume = Assert.Single(catalog.Volumes);
        Assert.Equal("My Disk", volume.Name);
        Assert.Equal("/dev/disk4", volume.Source);
        Assert.Equal(DiskCatalogMakerV5VolumeKind.Disk, volume.Kind);
        Assert.Equal(DiskCatalogMakerV5FileSystem.HfsPlus, volume.FileSystem);
        Assert.Equal(500_000_000_000UL, volume.CapacityBytes);
        Assert.Equal(Timestamp(CreatedUnix), Timestamp(volume.Created!.Value));
        Assert.Equal(Timestamp(ScannedUnix), Timestamp(volume.Scanned!.Value));

        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);
        Assert.Equal(3, document.Count);

        var media = Find(document, "media:7");
        var mediaProperties = Properties(media);
        Assert.Null(media["parentId"]);
        Assert.Equal("media", media["kind"]);
        Assert.Equal("Disk", mediaProperties["Kind"]);
        Assert.Equal("HFS+", mediaProperties["FileSystem"]);
        Assert.Equal("500000000000", mediaProperties["CapacityBytes"]);
        Assert.Equal("/dev/disk4", mediaProperties["Source"]);

        var folder = Find(document, "item:7:10");
        Assert.Equal("media:7", folder["parentId"]);
        Assert.Equal("folder", folder["kind"]);
        Assert.Equal("ドキュメント", folder["name"]);
        Assert.Equal("My Disk/ドキュメント", Properties(folder)["CatalogPath"]);

        var file = Find(document, "item:7:11");
        var fileProperties = Properties(file);
        Assert.Equal("item:7:10", file["parentId"]);
        Assert.Equal("file", file["kind"]);
        Assert.Equal("résumé.pdf", file["name"]);
        Assert.Equal("12345", file["sizeBytes"]);
        Assert.Equal("My Disk/ドキュメント/résumé.pdf", fileProperties["CatalogPath"]);
        Assert.Equal("PDF ", fileProperties["TypeCode"]);
        Assert.Equal("prvw", fileProperties["Creator"]);
        Assert.Equal("Résumé final", fileProperties["Comments"]);
        Assert.Equal("3", fileProperties["Label"]);
        Assert.Equal("Yellow", fileProperties["LabelName"]);
        Assert.Equal("0755", fileProperties["PosixMode"]);
        Assert.Equal(Timestamp(CreatedUnix), fileProperties["Created"]);
        Assert.Equal(Timestamp(ModifiedUnix), fileProperties["Modified"]);

        Assert.Equal(1, document.Summary.VolumeCount);
        Assert.Equal(2, document.Summary.ItemCount);
        Assert.Equal(2, document.Summary.ImportedItemCount);
    }

    [Fact]
    public void Parse_MultipleVolumes_KeepDuplicateNamesInSeparateRoots()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Archive");
        builder.AddVolume(2, "Backup");
        builder.AddItem(1, 100, null, "Photos", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(1, 101, 100, "pic.jpg", sizeBytes: 10);
        builder.AddItem(2, 100, null, "Photos", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(2, 101, 100, "pic.jpg", sizeBytes: 20);

        var document = DiskCatalogMakerV5HierarchyBuilder.Build(
            DiskCatalogMakerV5Parser.Parse(builder.Build()));

        Assert.Equal(6, document.Count);
        Assert.Equal("media:1", Find(document, "item:1:100")["parentId"]);
        Assert.Equal("media:2", Find(document, "item:2:100")["parentId"]);
        Assert.Equal("item:1:100", Find(document, "item:1:101")["parentId"]);
        Assert.Equal("item:2:100", Find(document, "item:2:101")["parentId"]);
        Assert.Equal("Archive", Find(document, "media:1")["name"]);
        Assert.Equal("Backup", Find(document, "media:2")["name"]);
        Assert.Equal("10", Find(document, "item:1:101")["sizeBytes"]);
        Assert.Equal("20", Find(document, "item:2:101")["sizeBytes"]);
        Assert.Equal("Archive/Photos/pic.jpg", Properties(Find(document, "item:1:101"))["CatalogPath"]);
        Assert.Equal("Backup/Photos/pic.jpg", Properties(Find(document, "item:2:101"))["CatalogPath"]);
    }

    [Fact]
    public void Parse_Utf16Strings_AreDecoded()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(
            1,
            "ボリューム",
            nameEncoding: DiskCatalogMakerV5Format.Utf16LittleEndianEncoding);
        builder.AddItem(
            1,
            2,
            null,
            "ファイル.txt",
            nameEncoding: DiskCatalogMakerV5Format.Utf16LittleEndianEncoding);

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());

        Assert.Equal("ボリューム", Assert.Single(catalog.Volumes).Name);
        Assert.Equal("ファイル.txt", Assert.Single(catalog.Items).Name);
    }

    [Fact]
    public void Parse_UnknownTables_ArePreservedWithWarning()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddUnknownTable(0x0100, 7, [1, 2, 3, 4], recordCount: 2);

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());

        var unknown = Assert.Single(catalog.UnknownTables);
        Assert.Equal(0x0100u, unknown.TableId);
        Assert.Equal(7u, unknown.HeaderValue);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, unknown.Payload);
        Assert.Contains(catalog.Warnings, warning => warning.Contains("unknown table 0x100", StringComparison.Ordinal));
        Assert.Contains(catalog.Tables, table => table.TableId == 0x0100 && !table.IsKnown);

        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);
        Assert.Equal(1, document.Summary.UnknownTableCount);
        Assert.Single(document.Summary.UnknownTables);
    }

    [Fact]
    public void Parse_AliasMetadata_IsMappedToItemProperties()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Archive");
        builder.AddItem(1, 20, null, "Documents", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(
            1,
            21,
            null,
            "Backup alias",
            DiskCatalogMakerV5ItemKind.Alias,
            aliasTargetItemId: 20);
        builder.AddAlias(
            50,
            21,
            targetVolumeId: 9,
            targetDirectoryId: 20,
            mountPoint: "/Volumes/Backup",
            posixPath: "Backup:Users:me",
            server: "nas.local",
            user: "me",
            zone: "local",
            unicodeName: "Résumé",
            unicodeVolumeName: "Backup",
            volumeCreatedUnix: CreatedUnix,
            fileCreatedUnix: ModifiedUnix,
            extraBytes: [9, 8, 7]);

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());
        var alias = Assert.Single(catalog.Aliases);
        Assert.Equal("/Volumes/Backup", alias.MountPoint);
        Assert.Equal(9u, alias.TargetVolumeId);
        Assert.Equal(new byte[] { 9, 8, 7 }, alias.RecursiveExtraBytes);

        var row = Find(
            DiskCatalogMakerV5HierarchyBuilder.Build(catalog),
            "item:1:21");
        var properties = Properties(row);
        Assert.Equal("file", row["kind"]);
        Assert.Equal("true", properties["Alias"]);
        Assert.Equal("20", properties["AliasTargetItemId"]);
        Assert.Equal("Archive/Documents", properties["AliasTargetPath"]);
        Assert.Equal("9", properties["AliasTargetVolumeId"]);
        Assert.Equal("20", properties["AliasTargetDirectoryId"]);
        Assert.Equal("/Volumes/Backup", properties["AliasMountPoint"]);
        Assert.Equal("Backup:Users:me", properties["AliasPosixPath"]);
        Assert.Equal("nas.local", properties["AliasServer"]);
        Assert.Equal("me", properties["AliasUser"]);
        Assert.Equal("local", properties["AliasZone"]);
        Assert.Equal("Résumé", properties["AliasUnicodeName"]);
        Assert.Equal("Backup", properties["AliasUnicodeVolumeName"]);
        Assert.Equal("3", properties["AliasExtraBytes"]);
        Assert.Equal(Timestamp(CreatedUnix), properties["AliasVolumeCreated"]);
        Assert.Equal(Timestamp(ModifiedUnix), properties["AliasFileCreated"]);
    }

    [Fact]
    public void Parse_MissingParentsAndCycles_AttachToVolumeRootWithWarnings()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 30, 999, "lost.txt");
        builder.AddItem(1, 31, 32, "left", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(1, 32, 31, "right", DiskCatalogMakerV5ItemKind.Directory);

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());
        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);

        Assert.Equal("media:1", Find(document, "item:1:30")["parentId"]);
        Assert.Equal("media:1", Find(document, "item:1:31")["parentId"]);
        Assert.Equal("media:1", Find(document, "item:1:32")["parentId"]);
        Assert.Contains(document.Summary.Warnings,
            warning => warning.Contains("missing parent 999", StringComparison.Ordinal));
        Assert.Contains(document.Summary.Warnings,
            warning => warning.Contains("parent cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_OutOfRangeDatesAndUnknownKinds_AreReportedButDoNotFail()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 40, null, "future.txt", createdUnix: 253_402_300_800);
        builder.AddItem(1, 41, null, "odd.bin", kind: (DiskCatalogMakerV5ItemKind)9);

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());
        var future = catalog.Items.Single(item => item.ItemId == 40);
        var odd = catalog.Items.Single(item => item.ItemId == 41);

        Assert.Null(future.Created);
        Assert.Equal(DiskCatalogMakerV5ItemKind.File, odd.Kind);
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("outside the supported range", StringComparison.Ordinal));
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("unknown kind 9", StringComparison.Ordinal));

        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);
        Assert.Equal("file", Find(document, "item:1:41")["kind"]);
    }

    [Fact]
    public void Parse_UnknownRequiredName_FallsBackAndUnknownOptionalValueIsOmitted()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "named.txt", fullPath: "/Volumes/Disk/named.txt");
        var payload = builder.Build();

        var itemTable = DiskCatalogMakerV5FixtureBuilder.FindTableOffset(
            payload, DiskCatalogMakerV5Format.VolumeDirTableId);
        var record = itemTable + DiskCatalogMakerV5Format.TableHeaderSize;
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, record + 12, 9999);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(payload, record + 16, 9998);

        var catalog = DiskCatalogMakerV5Parser.Parse(payload);
        var item = Assert.Single(catalog.Items);

        Assert.Equal("Item 2", item.Name);
        Assert.Null(item.FullPath);
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("unknown string identifier 9999", StringComparison.Ordinal));
        Assert.Contains(catalog.Warnings,
            warning => warning.Contains("unknown string identifier 9998", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_FullPathOverridesSynthesizedPath_AndNonPrintableFourCcIsEscaped()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(
            1,
            2,
            10,
            "pic.jpg",
            typeCode: "\u0001ABC",
            creatorCode: "prvw",
            fullPath: "/Volumes/Disk/Photos/pic.jpg");

        var document = DiskCatalogMakerV5HierarchyBuilder.Build(
            DiskCatalogMakerV5Parser.Parse(builder.Build()));
        var properties = Properties(Find(document, "item:1:2"));

        Assert.Equal("/Volumes/Disk/Photos/pic.jpg", properties["CatalogPath"]);
        Assert.Equal("\\x01ABC", properties["TypeCode"]);
        Assert.Equal("prvw", properties["Creator"]);
    }

    [Fact]
    public void Build_OrphanVolumeItems_GetsSynthesizedMediaNode()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddItem(5, 50, null, "orphan.txt", sizeBytes: 42);

        var catalog = DiskCatalogMakerV5Parser.Parse(builder.Build());
        var document = DiskCatalogMakerV5HierarchyBuilder.Build(catalog);

        var media = Find(document, "media:5");
        Assert.Equal("Volume 5", media["name"]);
        Assert.Equal("media", media["kind"]);
        Assert.Equal("media:5", Find(document, "item:5:50")["parentId"]);
        Assert.Equal(0, document.Summary.VolumeCount);
        Assert.Equal(1, document.Summary.ImportedItemCount);
        Assert.Contains(document.Summary.Warnings,
            warning => warning.Contains("a media node was synthesized", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_PackageItem_IsFolderAndCarriesPackageFlag()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "Photos.photoslibrary", DiskCatalogMakerV5ItemKind.Package);

        var row = Find(
            DiskCatalogMakerV5HierarchyBuilder.Build(DiskCatalogMakerV5Parser.Parse(builder.Build())),
            "item:1:2");

        Assert.Equal("folder", row["kind"]);
        var properties = Properties(row);
        Assert.Equal("true", properties["Package"]);
        Assert.Equal("Package", properties["Kind"]);
    }

    private static Dictionary<string, object?> Find(DiskCatalogMakerV5ReportDocument document, string nodeId)
    {
        return Assert.Single(document.Rows, row => (string?)row["nodeId"] == nodeId);
    }

    private static Dictionary<string, object?> Properties(Dictionary<string, object?> row)
    {
        return Assert.IsType<Dictionary<string, object?>>(row["properties"]);
    }

    private static string Timestamp(long unixSeconds)
    {
        return Timestamp(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
    }

    private static string Timestamp(DateTimeOffset value)
    {
        return value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
