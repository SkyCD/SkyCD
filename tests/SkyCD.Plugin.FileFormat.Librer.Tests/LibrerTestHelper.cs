using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using MessagePack;
using ZstdSharp;

namespace SkyCD.Plugin.FileFormat.Librer.Tests;

public static class LibrerTestHelper
{
    public static void CreateSampleLibrerCatalog(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var zipStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        // 1. Create HEADER entry (MessagePack raw)
        var headerDict = new Dictionary<string, object>
        {
            ["label"] = "ArchiveDisk2024",
            ["scan_path"] = "/media/backup",
            ["info"] = "Backup of photos and documents",
            ["creation_time"] = 1716120000L,
            ["creation_host"] = "main-workstation",
            ["creation_os"] = "Linux 6.8.0",
            ["quant_files"] = 3L,
            ["quant_folders"] = 2L,
            ["sum_size"] = 1550000L,
            ["free"] = 50000000000L,
            ["data_format_version"] = 1,
            ["compression_level"] = 9,
            ["items_names"] = 5,
            ["items_cd"] = 2
        };
        var headerBytes = MessagePackSerializer.Serialize(headerDict);
        var headerEntry = zipArchive.CreateEntry("HEADER", CompressionLevel.Optimal);
        using (var entryStream = headerEntry.Open())
        {
            entryStream.Write(headerBytes);
        }

        // 2. Create filenames entry (MessagePack array, Zstd compressed)
        var filenames = new object[]
        {
            "Documents", // 0
            "Photos",    // 1
            "report.pdf",// 2
            "notes.txt",  // 3
            "sunset.jpg" // 4
        };
        var filenamesBytes = MessagePackSerializer.Serialize(filenames);
        using var compressor = new Compressor(9);
        var compressedFilenames = compressor.Wrap(filenamesBytes).ToArray();

        var filenamesEntry = zipArchive.CreateEntry("filenames", CompressionLevel.NoCompression);
        using (var entryStream = filenamesEntry.Open())
        {
            entryStream.Write(compressedFilenames);
        }

        // 3. Create customdata entry (MessagePack array, Zstd compressed)
        var customData = new object[]
        {
            new object[] { 0, 0, "PDF Document / 12 pages" },
            new object[] { 0, 0, "EXIF: ISO 100, f/2.8, 1/500s" }
        };
        var customDataBytes = MessagePackSerializer.Serialize(customData);
        var compressedCustomData = compressor.Wrap(customDataBytes).ToArray();

        var customDataEntry = zipArchive.CreateEntry("customdata", CompressionLevel.NoCompression);
        using (var entryStream = customDataEntry.Open())
        {
            entryStream.Write(compressedCustomData);
        }

        // 4. Create filestructure entry (MessagePack tuple hierarchy, Zstd compressed)
        // Code flags:
        // Folder: 528 (512 is_dir + 16 has_files)
        // File: 256 (256 is_file)
        // File with custom data: 288 (256 is_file + 32 has_cd)
        var fileReport = new object[] { 2, 288, 500000L, 1716100000L, 0 }; // cd index 0
        var fileNotes = new object[] { 3, 256, 50000L, 1716105000L };
        var fileSunset = new object[] { 4, 288, 1000000L, 1716112000L, 1 }; // cd index 1

        var folderDocs = new object[]
        {
            0,
            528,
            550000L,
            1716110000L,
            new object[] { fileReport, fileNotes }
        };

        var folderPhotos = new object[]
        {
            1,
            528,
            1000000L,
            1716115000L,
            new object[] { fileSunset }
        };

        var rootStructure = new object[]
        {
            "",
            528,
            1550000L,
            1716120000L,
            new object[] { folderDocs, folderPhotos }
        };

        var filestructureBytes = MessagePackSerializer.Serialize(rootStructure);
        var compressedFilestructure = compressor.Wrap(filestructureBytes).ToArray();

        var filestructureEntry = zipArchive.CreateEntry("filestructure", CompressionLevel.NoCompression);
        using (var entryStream = filestructureEntry.Open())
        {
            entryStream.Write(compressedFilestructure);
        }
    }

    public static void CreateEmptyFilestructureCatalog(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        using var zipStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create);

        var headerDict = new Dictionary<string, object>
        {
            ["label"] = "EmptyDisk",
            ["sum_size"] = 0L,
            ["quant_files"] = 0L,
            ["quant_folders"] = 0L
        };
        var headerBytes = MessagePackSerializer.Serialize(headerDict);
        var headerEntry = zipArchive.CreateEntry("HEADER", CompressionLevel.Optimal);
        using (var entryStream = headerEntry.Open())
        {
            entryStream.Write(headerBytes);
        }

        var filenamesEntry = zipArchive.CreateEntry("filenames", CompressionLevel.Optimal);
        using (var entryStream = filenamesEntry.Open())
        {
            entryStream.Write(MessagePackSerializer.Serialize(Array.Empty<string>()));
        }

        var filestructureEntry = zipArchive.CreateEntry("filestructure", CompressionLevel.Optimal);
        using (var entryStream = filestructureEntry.Open())
        {
            entryStream.Write(Array.Empty<byte>());
        }
    }

    public static void EnsureFixtureFilesExist(string targetDirectory)
    {
        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var samplePath = Path.Combine(targetDirectory, "sample.librer_data");
        if (!File.Exists(samplePath))
        {
            CreateSampleLibrerCatalog(samplePath);
        }
    }
}
