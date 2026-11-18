using System;
using System.IO;
using SkyCD.Plugin.DiskCatalogMaker;

namespace SkyCD.Plugin.DiskCatalogMaker.Tests;

/// <summary>
/// Synthetic, license-safe DiskCatalogMaker v5 payloads used as committed fixtures.
/// The writer exists only in the test project; the plugin itself is read-only.
/// </summary>
internal static class DiskCatalogMakerV5Fixtures
{
    public const string FixtureDirectory = "Fixtures/DiskCatalogMakerV5";

    private const long CreatedUnix = 1_700_000_000;
    private const long ModifiedUnix = 1_700_100_000;
    private const long ScannedUnix = 1_700_200_000;

    public static byte[] EmptyCatalog() => new DiskCatalogMakerV5FixtureBuilder().Build();

    public static byte[] SingleVolumeUnicode()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder { Revision = 3 };
        builder.AddVolume(
            1,
            "Archive Disk",
            kind: DiskCatalogMakerV5VolumeKind.Disk,
            fileSystem: DiskCatalogMakerV5FileSystem.HfsPlus,
            capacityBytes: 500_000_000_000,
            freeBytes: 100_000_000_000,
            createdUnix: CreatedUnix,
            scannedUnix: ScannedUnix,
            source: "/dev/disk4");
        builder.AddItem(1, 10, null, "ドキュメント", DiskCatalogMakerV5ItemKind.Directory, createdUnix: CreatedUnix);
        builder.AddItem(1, 11, 10, "Photos", DiskCatalogMakerV5ItemKind.Directory, createdUnix: CreatedUnix);
        builder.AddItem(
            1,
            12,
            11,
            "zero-byte.txt",
            sizeBytes: 0,
            typeCode: "TEXT",
            creatorCode: "ttxt",
            comment: "Empty placeholder",
            label: 2,
            posixMode: Posix("644"),
            modifiedUnix: ModifiedUnix);
        builder.AddItem(
            1,
            13,
            11,
            "backup.iso",
            sizeBytes: 5_000_000_000_000,
            createdUnix: CreatedUnix,
            modifiedUnix: ModifiedUnix,
            scannedUnix: ScannedUnix,
            typeCode: "ISOb",
            creatorCode: "ttxt",
            comment: "Large archive",
            label: 5,
            posixMode: Posix("644"));
        builder.AddItem(
            1,
            14,
            10,
            "résumé.pdf",
            sizeBytes: 12345,
            createdUnix: CreatedUnix,
            modifiedUnix: ModifiedUnix,
            typeCode: "PDF ",
            creatorCode: "prvw",
            comment: "Résumé final",
            label: 3,
            posixMode: Posix("755"));
        return builder.Build();
    }

    public static byte[] MultipleVolumes()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Archive Disk");
        builder.AddVolume(2, "Backup 2026");
        builder.AddItem(1, 10, null, "Documents", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(1, 11, 10, "readme.txt", sizeBytes: 10);
        builder.AddItem(1, 12, 10, "grüße.txt", sizeBytes: 20);
        builder.AddItem(2, 10, null, "Photos", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(2, 11, 10, "readme.txt", sizeBytes: 30);
        builder.AddItem(2, 12, 10, "pic.jpg", sizeBytes: 40);
        return builder.Build();
    }

    public static byte[] Aliases()
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
        return builder.Build();
    }

    public static byte[] UnknownTable()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "Disk");
        builder.AddItem(1, 2, null, "file.txt", sizeBytes: 10);
        builder.AddUnknownTable(0x0100, 7, [0xDE, 0xAD, 0xBE, 0xEF], recordCount: 1);
        return builder.Build();
    }

    public static byte[] ThumbnailCatalog()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder();
        builder.AddVolume(1, "写真アーカイブ", kind: DiskCatalogMakerV5VolumeKind.DiskImage);
        builder.AddItem(1, 2, null, "Photos", DiskCatalogMakerV5ItemKind.Directory);
        builder.AddItem(
            1,
            3,
            2,
            "pic.jpg",
            sizeBytes: 512,
            comment: "サムネイル",
            thumbnailKey: 0x0A0B0C0D);
        builder.AddThumbnail(0x0A0B0C0D, 3, "png");
        return builder.Build();
    }

    public static byte[] ThumbnailPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    public static byte[] CorruptTruncatedHeader() => SingleVolumeUnicode()[..10];

    public static byte[] CorruptTruncatedTable() => SingleVolumeUnicode()[..^4];

    public static byte[] CorruptInvalidCount()
    {
        var payload = SingleVolumeUnicode();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(
            payload,
            DiskCatalogMakerV5Format.HeaderSize + 12,
            6_000_000);
        return payload;
    }

    public static byte[] CorruptInvalidOffset()
    {
        var payload = SingleVolumeUnicode();
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(
            payload,
            DiskCatalogMakerV5Format.HeaderSize + 4,
            (uint)payload.Length + 4);
        return payload;
    }

    public static byte[] CorruptOverlap()
    {
        var payload = SingleVolumeUnicode();
        var firstOffset = DiskCatalogMakerV5FixtureBuilder.ReadUInt32(
            payload,
            DiskCatalogMakerV5Format.HeaderSize + 4);
        DiskCatalogMakerV5FixtureBuilder.PatchUInt32(
            payload,
            DiskCatalogMakerV5Format.HeaderSize + DiskCatalogMakerV5Format.DirectoryEntrySize + 4,
            firstOffset);
        return payload;
    }

    public static byte[] UnsupportedVersion()
    {
        var builder = new DiskCatalogMakerV5FixtureBuilder { MajorVersion = 6 };
        builder.AddVolume(1, "Disk");
        return builder.Build();
    }

    public static void WriteAll(string root)
    {
        Directory.CreateDirectory(root);
        Write(root, "empty.dcmf", EmptyCatalog());
        Write(root, "single-volume-unicode.dcmf", SingleVolumeUnicode());
        Write(root, "multiple-volumes.dcmf", MultipleVolumes());
        Write(root, "aliases.dcmf", Aliases());
        Write(root, "unknown-table.dcmf", UnknownTable());
        Write(root, "corrupt-truncated-header.dcmf", CorruptTruncatedHeader());
        Write(root, "corrupt-truncated-table.dcmf", CorruptTruncatedTable());
        Write(root, "corrupt-invalid-count.dcmf", CorruptInvalidCount());
        Write(root, "corrupt-invalid-offset.dcmf", CorruptInvalidOffset());
        Write(root, "corrupt-overlap.dcmf", CorruptOverlap());
        Write(root, "unsupported-version.dcmf", UnsupportedVersion());

        var withThumbnails = Path.Combine(root, "package-with-thumbnails");
        Directory.CreateDirectory(Path.Combine(withThumbnails, "Thumbnails", "0a", "0b", "0c"));
        Write(withThumbnails, "Catalog.dcm", ThumbnailCatalog());
        Write(
            Path.Combine(withThumbnails, "Thumbnails", "0a", "0b", "0c"),
            "0d.png",
            ThumbnailPng());

        var withoutThumbnails = Path.Combine(root, "package-without-thumbnails");
        Directory.CreateDirectory(withoutThumbnails);
        Write(withoutThumbnails, "Catalog.dcm", ThumbnailCatalog());
    }

    private static void Write(string root, string name, byte[] payload)
    {
        File.WriteAllBytes(Path.Combine(root, name), payload);
    }

    private static int Posix(string octal) => Convert.ToInt32(octal, 8);
}
