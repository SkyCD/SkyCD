using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.Cathy.Exceptions;
using SkyCD.Plugin.FileFormat.Cathy.Models;

namespace SkyCD.Plugin.FileFormat.Cathy.Parsing;

/// <summary>
/// Binary parser for Cathy disk catalog (.caf) files.
/// </summary>
public static class CathyParser
{
    public const uint MagicBase = 500410407;
    public const uint Modus = 1000000000;
    public const short SupportedMaxVersion = 9;

    /// <summary>
    /// Checks if the stream begins with a valid Cathy catalog magic number.
    /// Preserves stream position.
    /// </summary>
    public static bool IsCathyStream(Stream stream)
    {
        if (!stream.CanRead) return false;
        var initialPos = stream.CanSeek ? stream.Position : -1;
        try
        {
            var magicBytes = new byte[4];
            var read = stream.Read(magicBytes, 0, 4);
            if (read < 4) return false;

            var ul = BitConverter.ToUInt32(magicBytes, 0);
            return ul > 0 && (ul % Modus) == MagicBase;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (initialPos >= 0 && stream.CanSeek)
            {
                stream.Position = initialPos;
            }
        }
    }

    /// <summary>
    /// Parses a Cathy (.caf) binary catalog stream.
    /// </summary>
    public static CathyCatalog Parse(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.Latin1, leaveOpen: true);

        if (stream.Length - stream.Position < 4)
        {
            throw new CathyCorruptHeaderException("File is too small to contain a valid Cathy header.");
        }

        var magic = reader.ReadUInt32();
        if (magic == 0 || (magic % Modus) != MagicBase)
        {
            throw new CathyCorruptHeaderException($"Invalid magic number: {magic}. Expected Cathy catalog signature.");
        }

        var baseVersion = (short)(magic / Modus);
        short version = baseVersion;

        if (baseVersion > 2)
        {
            if (stream.Position + 2 > stream.Length)
            {
                throw new CathyCorruptHeaderException("Premature EOF while reading Cathy extended version.");
            }
            version = reader.ReadInt16();
        }

        if (version > SupportedMaxVersion || version < 0)
        {
            throw new CathyIncompatibleVersionException(version);
        }

        // Header fields
        DateTime? catalogDate = null;
        if (stream.Position + 4 <= stream.Length)
        {
            var timeUnix = reader.ReadUInt32();
            if (timeUnix > 0)
            {
                try
                {
                    catalogDate = DateTimeOffset.FromUnixTimeSeconds(timeUnix).LocalDateTime;
                }
                catch
                {
                    // Ignore date parse issues
                }
            }
        }

        string? device = null;
        if (version >= 2)
        {
            device = ReadNullTerminatedString(reader);
        }

        var volume = ReadNullTerminatedString(reader);
        var alias = ReadNullTerminatedString(reader);

        string? volumeName = null;
        if (string.IsNullOrWhiteSpace(alias))
        {
            volumeName = !string.IsNullOrWhiteSpace(volume) ? volume : device;
        }
        else
        {
            volumeName = $"{alias} ({volume})";
        }

        string? serialNumber = null;
        if (stream.Position + 4 <= stream.Length)
        {
            var serial = reader.ReadUInt32();
            if (serial > 0)
            {
                serialNumber = $"{serial:X8}";
                if (serialNumber.Length == 8)
                {
                    serialNumber = $"{serialNumber[..4]}-{serialNumber[4..]}";
                }
            }
        }

        string? comment = null;
        if (version >= 4)
        {
            comment = ReadNullTerminatedString(reader);
        }

        uint freeSizeMb = 0;
        if (version >= 1 && stream.Position + 4 <= stream.Length)
        {
            freeSizeMb = reader.ReadUInt32();
        }

        short archiveFlag = 0;
        if (version >= 6 && stream.Position + 2 <= stream.Length)
        {
            archiveFlag = reader.ReadInt16();
            if (archiveFlag == -1) archiveFlag = 0;
        }

        var header = new CathyCatalogHeader(
            Version: version,
            CatalogDate: catalogDate,
            Device: device,
            Volume: volume,
            Alias: alias,
            VolumeName: volumeName,
            SerialNumber: serialNumber,
            Comment: comment,
            FreeSizeMb: freeSizeMb,
            ArchiveFlag: archiveFlag);

        // Folders list
        if (stream.Position + 4 > stream.Length)
        {
            throw new CathyInvalidCatalogDataException("Unexpected end of file before folders table.");
        }

        var folderCount = reader.ReadInt32();
        if (folderCount < 0 || folderCount > 10_000_000)
        {
            throw new CathyInvalidCatalogDataException($"Invalid folder count: {folderCount}.");
        }

        var folders = new List<CathyFolderRecord>(Math.Min(folderCount, 65536));
        for (var i = 0; i < folderCount; i++)
        {
            string folderName = string.Empty;
            if (i == 0 || version <= 3)
            {
                folderName = ReadNullTerminatedString(reader);
            }

            int filesInFolder = 0;
            double folderTotalSize = 0;

            if (version >= 3)
            {
                if (stream.Position + 12 > stream.Length)
                {
                    throw new CathyInvalidCatalogDataException($"Premature EOF reading folder record {i}.");
                }
                filesInFolder = reader.ReadInt32();
                folderTotalSize = reader.ReadDouble();
            }

            folders.Add(new CathyFolderRecord(i, folderName, filesInFolder, folderTotalSize));
        }

        // File list
        if (stream.Position + 4 > stream.Length)
        {
            throw new CathyInvalidCatalogDataException("Unexpected end of file before files table.");
        }

        var fileCount = reader.ReadInt32();
        if (fileCount < 0 || fileCount > 50_000_000)
        {
            throw new CathyInvalidCatalogDataException($"Invalid file count: {fileCount}.");
        }

        var entries = new List<CathyFileEntry>(Math.Min(fileCount, 65536));
        for (var i = 0; i < fileCount; i++)
        {
            if (stream.Position + 4 > stream.Length)
            {
                throw new CathyInvalidCatalogDataException($"Premature EOF reading file entry {i} timestamp.");
            }

            var rawDate = reader.ReadUInt32();
            DateTime? modifiedDate = null;
            if (rawDate > 0)
            {
                try
                {
                    modifiedDate = DateTimeOffset.FromUnixTimeSeconds(rawDate).LocalDateTime;
                }
                catch
                {
                    // Ignore date parse issues
                }
            }

            long size = 0;
            if (version > 6)
            {
                if (stream.Position + 8 > stream.Length)
                {
                    throw new CathyInvalidCatalogDataException($"Premature EOF reading file entry {i} size (8 bytes).");
                }
                size = reader.ReadInt64();
            }
            else
            {
                if (stream.Position + 4 > stream.Length)
                {
                    throw new CathyInvalidCatalogDataException($"Premature EOF reading file entry {i} size (4 bytes).");
                }
                size = reader.ReadInt32();
            }

            int parentFolderId;
            if (version > 7)
            {
                if (stream.Position + 4 > stream.Length)
                {
                    throw new CathyInvalidCatalogDataException($"Premature EOF reading file entry {i} parent folder ID (4 bytes).");
                }
                parentFolderId = reader.ReadInt32();
            }
            else
            {
                if (stream.Position + 2 > stream.Length)
                {
                    throw new CathyInvalidCatalogDataException($"Premature EOF reading file entry {i} parent folder ID (2 bytes).");
                }
                parentFolderId = reader.ReadUInt16();
            }

            var name = ReadNullTerminatedString(reader);

            bool isDirectory = false;
            int directoryId = 0;
            if (size < 0)
            {
                isDirectory = true;
                directoryId = (int)(-size);
                size = 0;
            }

            entries.Add(new CathyFileEntry(modifiedDate, size, parentFolderId, name, isDirectory, directoryId));
        }

        return new CathyCatalog(header, folders, entries);
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>(32);
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var b = reader.ReadByte();
            if (b == 0) break;
            bytes.Add(b);
        }

        if (bytes.Count == 0) return string.Empty;

        // Try UTF-8 first, fallback to Latin1
        try
        {
            var utf8Str = Encoding.UTF8.GetString(bytes.ToArray());
            if (!utf8Str.Contains('\uFFFD'))
            {
                return utf8Str;
            }
        }
        catch
        {
            // fallback
        }

        return Encoding.Latin1.GetString(bytes.ToArray());
    }
}
