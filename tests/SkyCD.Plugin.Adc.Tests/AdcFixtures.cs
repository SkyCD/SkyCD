using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Adc;
using SkyCD.Plugin.Adc.Models;

namespace SkyCD.Plugin.Adc.Tests;

/// <summary>
/// Synthetic, license-safe fixture catalogs plus the human-readable manifest.
/// </summary>
public static class AdcFixtures
{
    public const string FixtureDirectory = "Fixtures";

    public static AdcCatalog EmptyCatalog() => new()
    {
        VersionMajor = 1,
        VersionMinor = 0,
        Flags = 0
    };

    public static AdcCatalog NestedCatalog()
    {
        var catalog = new AdcCatalog
        {
            VersionMajor = 1,
            VersionMinor = 0,
            Flags = 1
        };
        catalog.Volumes.Add(new AdcVolume
        {
            VolumeId = 1,
            VolumeType = AdcVolumeType.Fixed,
            Flags = 3,
            SerialNumber = 0x1A2B3C4D,
            CapacityBytes = 512_000_000_000,
            Label = "DATA",
            Source = "D:\\"
        });

        catalog.Items.Add(new AdcItem
        {
            ItemId = 10,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            Name = "Projects"
        });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 11,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            Name = "Empty"
        });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 12,
            ParentId = 10,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            Name = "src"
        });

        var main = new AdcItem
        {
            ItemId = 13,
            ParentId = 12,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Attributes = 0x20,
            TypeCode = 7,
            SizeBytes = 1234,
            CreatedFileTime = FileTime(2024, 5, 1, 12, 0, 0),
            ModifiedFileTime = FileTime(2024, 5, 2, 8, 30, 0),
            AccessedFileTime = FileTime(2024, 5, 3, 9, 45, 0),
            Name = "main.cs"
        };
        main.Properties.Add(new AdcProperty(0x05, 99u));
        main.Properties.Add(new AdcProperty(0x12, "custom-note"));
        main.Properties.Add(new AdcProperty(0x2A, new byte[] { 1, 2, 3, 4 }));
        catalog.Items.Add(main);

        catalog.Items.Add(new AdcItem
        {
            ItemId = 14,
            ParentId = 10,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Attributes = 0x01,
            SizeBytes = 42,
            Name = "readme.txt"
        });

        return catalog;
    }

    public static AdcCatalog MultiVolumeCatalog()
    {
        var catalog = new AdcCatalog
        {
            VersionMajor = 1,
            VersionMinor = 2,
            Flags = 0
        };
        catalog.Volumes.Add(new AdcVolume
        {
            VolumeId = 1,
            VolumeType = AdcVolumeType.Dvd,
            SerialNumber = 0xDEADBEEF,
            CapacityBytes = 8_000_000_000,
            Label = "АРХИВ",
            Source = "F:\\"
        });
        catalog.Volumes.Add(new AdcVolume
        {
            VolumeId = 2,
            VolumeType = AdcVolumeType.Removable,
            Flags = 1,
            CapacityBytes = 4_000_000_000,
            Label = "日本語"
        });
        catalog.Categories.Add(new AdcCategory { CategoryId = 1, Name = "Архив" });

        catalog.Items.Add(new AdcItem
        {
            ItemId = 20,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            Name = "Документы"
        });
        var report = new AdcItem
        {
            ItemId = 21,
            ParentId = 20,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Attributes = 0x20,
            TypeCode = 2,
            SizeBytes = 12,
            Name = "отчёт.txt"
        };
        report.CategoryIds.Add(1);
        catalog.Items.Add(report);
        catalog.Items.Add(new AdcItem
        {
            ItemId = 22,
            ParentId = 0,
            VolumeId = 2,
            Kind = AdcItemKind.File,
            Attributes = 0x20,
            TypeCode = 9,
            SizeBytes = 5_368_709_120,
            Name = "huge.iso"
        });
        catalog.Items.Add(new AdcItem
        {
            ItemId = 23,
            ParentId = 0,
            VolumeId = 2,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            Name = "Пусто"
        });

        return catalog;
    }

    public static AdcCatalog CommentsCatalog()
    {
        var catalog = new AdcCatalog
        {
            VersionMajor = 1,
            VersionMinor = 0
        };
        catalog.Volumes.Add(new AdcVolume
        {
            VolumeId = 1,
            VolumeType = AdcVolumeType.CdRom,
            SerialNumber = 0x00001234,
            CapacityBytes = 700_000_000,
            Label = "CD-ROM",
            Source = "E:"
        });
        catalog.Categories.Add(new AdcCategory { CategoryId = 1, Name = "Games" });
        catalog.Categories.Add(new AdcCategory { CategoryId = 2, Name = "Utilities" });
        catalog.Comments.Add("Folder A description");
        catalog.Comments.Add("Installer setup");
        catalog.Comments.Add("Duplicate setup under B");

        catalog.Items.Add(new AdcItem
        {
            ItemId = 30,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            CommentIndex = 1,
            Name = "A"
        });
        var setupA = new AdcItem
        {
            ItemId = 31,
            ParentId = 30,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Attributes = 0x20,
            TypeCode = 1,
            SizeBytes = 100,
            CreatedFileTime = FileTime(2023, 12, 24, 18, 0, 0),
            CommentIndex = 2,
            Name = "setup.exe"
        };
        setupA.CategoryIds.Add(1);
        setupA.CategoryIds.Add(2);
        catalog.Items.Add(setupA);

        catalog.Items.Add(new AdcItem
        {
            ItemId = 32,
            ParentId = 0,
            VolumeId = 1,
            Kind = AdcItemKind.Folder,
            Attributes = 0x10,
            Name = "B"
        });
        var setupB = new AdcItem
        {
            ItemId = 33,
            ParentId = 32,
            VolumeId = 1,
            Kind = AdcItemKind.File,
            Attributes = 0x20,
            TypeCode = 1,
            SizeBytes = 200,
            CommentIndex = 3,
            Name = "setup.exe"
        };
        setupB.CategoryIds.Add(2);
        catalog.Items.Add(setupB);

        return catalog;
    }

    public static IReadOnlyList<(string FileName, AdcCatalog Catalog)> ValidCatalogs() =>
    [
        ("empty.cat", EmptyCatalog()),
        ("nested.cat", NestedCatalog()),
        ("multivolume.cat", MultiVolumeCatalog()),
        ("comments.cat", CommentsCatalog())
    ];

    public static byte[] TruncatedNested()
    {
        var bytes = AdcFixtureWriter.Write(NestedCatalog());
        return bytes[..(AdcFormat.HeaderSize - 15)];
    }

    public static byte[] CorruptNested()
    {
        var bytes = AdcFixtureWriter.Write(NestedCatalog());
        bytes[AdcFormat.SignatureOffset] = 0x00;
        return bytes;
    }

    public static byte[] UnsupportedNested()
    {
        var bytes = AdcFixtureWriter.Write(NestedCatalog());
        bytes[AdcFormat.VersionMajorOffset] = 2;
        bytes[AdcFormat.VersionMajorOffset + 1] = 0;
        return bytes;
    }

    /// <summary>
    /// A DER-encoded ASN.1 SEQUENCE with long-form length, the shape of a Windows
    /// Authenticode security catalog (PKCS#7 SignedData with the signedData OID).
    /// </summary>
    public static byte[] WindowsSecurityCatalog()
    {
        var bytes = new byte[64];
        bytes[0] = 0x30;
        bytes[1] = 0x82;
        bytes[2] = 0x00;
        bytes[3] = 0x3C;
        byte[] signedDataOid = [0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x02];
        signedDataOid.CopyTo(bytes, 4);
        return bytes;
    }

    public static string BuildManifestJson()
    {
        var fixtures = new List<object>();
        foreach (var (fileName, catalog) in ValidCatalogs())
        {
            fixtures.Add(BuildFixtureManifest(fileName, catalog));
        }

        var manifest = new
        {
            note = "Synthetic Advanced Disk Catalog fixtures generated by AdcFixtureWriter; the manifest is the import oracle.",
            fixtures
        };

        return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static FileFormatReadRequest ReadRequest(Stream source, string fileName = "catalog.cat")
    {
        return new FileFormatReadRequest
        {
            Source = source,
            FormatId = "advanced-disk-catalog",
            FileName = fileName,
            SourcePath = fileName
        };
    }

    public static void WriteAll(string directory)
    {
        var malformedDirectory = Path.Combine(directory, "malformed");
        Directory.CreateDirectory(malformedDirectory);

        foreach (var (fileName, catalog) in ValidCatalogs())
        {
            File.WriteAllBytes(Path.Combine(directory, fileName), AdcFixtureWriter.Write(catalog));
        }

        File.WriteAllBytes(Path.Combine(malformedDirectory, "truncated.cat"), TruncatedNested());
        File.WriteAllBytes(Path.Combine(malformedDirectory, "corrupt.cat"), CorruptNested());
        File.WriteAllBytes(Path.Combine(malformedDirectory, "unsupported.cat"), UnsupportedNested());
        File.WriteAllBytes(Path.Combine(directory, "windows-security.cat"), WindowsSecurityCatalog());
        File.WriteAllText(Path.Combine(directory, "manifest.json"), BuildManifestJson());
    }

    private static Dictionary<string, object?> BuildFixtureManifest(string fileName, AdcCatalog catalog)
    {
        var itemsById = catalog.Items.ToDictionary(item => item.ItemId);
        var categoriesById = catalog.Categories.ToDictionary(category => category.CategoryId);
        var volumesById = catalog.Volumes.ToDictionary(volume => volume.VolumeId);

        return new Dictionary<string, object?>
        {
            ["file"] = fileName,
            ["formatVersion"] = $"{catalog.VersionMajor}.{catalog.VersionMinor}",
            ["recordCount"] = catalog.Volumes.Count + catalog.Categories.Count + catalog.Items.Count,
            ["commentCount"] = catalog.Comments.Count,
            ["categoryCount"] = catalog.Categories.Count,
            ["volumes"] = catalog.Volumes.Select(volume => new Dictionary<string, object?>
            {
                ["id"] = volume.VolumeId,
                ["label"] = volume.Label,
                ["type"] = DescribeVolumeType(volume.VolumeType),
                ["typeCode"] = (byte)volume.VolumeType,
                ["flags"] = volume.Flags,
                ["serial"] = $"0x{volume.SerialNumber:X8}",
                ["capacityBytes"] = volume.CapacityBytes.ToString(CultureInfo.InvariantCulture),
                ["source"] = volume.Source,
                ["properties"] = EncodeProperties(volume.Properties)
            }).ToArray(),
            ["categories"] = catalog.Categories.Select(category => new Dictionary<string, object?>
            {
                ["id"] = category.CategoryId,
                ["name"] = category.Name
            }).ToArray(),
            ["entries"] = catalog.Items.Select(item => new Dictionary<string, object?>
            {
                ["id"] = item.ItemId,
                ["volumeId"] = item.VolumeId,
                ["parentId"] = item.ParentId,
                ["path"] = BuildPath(item, itemsById, volumesById),
                ["kind"] = item.Kind == AdcItemKind.Folder ? "folder" : "file",
                ["name"] = item.Name,
                ["sizeBytes"] = item.SizeBytes.ToString(CultureInfo.InvariantCulture),
                ["createdUtc"] = FormatFileTime(item.CreatedFileTime),
                ["modifiedUtc"] = FormatFileTime(item.ModifiedFileTime),
                ["accessedUtc"] = FormatFileTime(item.AccessedFileTime),
                ["comment"] = item.CommentIndex == 0
                    ? null
                    : catalog.Comments[(int)item.CommentIndex - 1],
                ["categories"] = item.CategoryIds.Select(id => categoriesById[id].Name).ToArray(),
                ["categoryIds"] = item.CategoryIds.ToArray(),
                ["attributes"] = item.Attributes,
                ["typeCode"] = item.TypeCode,
                ["properties"] = EncodeProperties(item.Properties)
            }).ToArray()
        };
    }

    private static string BuildPath(
        AdcItem item,
        Dictionary<uint, AdcItem> itemsById,
        Dictionary<uint, AdcVolume> volumesById)
    {
        var segments = new List<string> { item.Name };
        var current = item;
        while (current.ParentId != 0)
        {
            current = itemsById[current.ParentId];
            segments.Insert(0, current.Name);
        }

        segments.Insert(0, volumesById[item.VolumeId].Label);
        return string.Join("/", segments);
    }

    private static string DescribeVolumeType(AdcVolumeType volumeType) =>
        volumeType switch
        {
            AdcVolumeType.Fixed => "Fixed",
            AdcVolumeType.Removable => "Removable",
            AdcVolumeType.Network => "Network",
            AdcVolumeType.CdRom => "CD-ROM",
            AdcVolumeType.Dvd => "DVD",
            AdcVolumeType.RamDisk => "RAM disk",
            _ => "Unknown"
        };

    private static Dictionary<string, string> EncodeProperties(List<AdcProperty> properties)
    {
        var encoded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (properties.Count == 0)
        {
            return encoded;
        }

        encoded["AdcPropertyOrder"] = string.Join(",", properties.Select(property => $"{property.Id:X2}"));
        foreach (var property in properties)
        {
            encoded[$"AdcProp_{property.Id:X2}"] = property.Kind switch
            {
                AdcPropertyKind.Uint32 => $"u:{property.UintValue.ToString(CultureInfo.InvariantCulture)}",
                AdcPropertyKind.String => $"s:{property.Text}",
                AdcPropertyKind.Blob => $"b:{Convert.ToBase64String(property.Blob ?? [])}",
                _ => string.Empty
            };
        }

        return encoded;
    }

    private static string? FormatFileTime(ulong fileTime) =>
        fileTime == 0
            ? null
            : DateTime.FromFileTimeUtc((long)fileTime).ToString("O", CultureInfo.InvariantCulture);

    private static ulong FileTime(int year, int month, int day, int hour, int minute, int second) =>
        (ulong)new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc).ToFileTimeUtc();
}
