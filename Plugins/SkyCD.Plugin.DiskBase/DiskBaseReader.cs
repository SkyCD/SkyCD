using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace SkyCD.Plugin.DiskBase;

internal sealed class DiskBaseReader(Stream source, Encoding encoding, CancellationToken cancellationToken)
{
    private readonly BinaryReader reader = new(source, Encoding.UTF8, leaveOpen: true);
    private readonly List<Dictionary<string, object?>> rows = [];
    private readonly List<(uint Offset, string ParentId, long DirectoryOffset)> fileLists = [];
    private readonly List<(uint Offset, Dictionary<string, object?> Properties)> descriptions = [];
    private int nodeCount;

    internal static void ValidateHeader(Stream stream)
    {
        if (!stream.CanRead || !stream.CanSeek || stream.Length < DiskBaseFormat.DataOffset)
        {
            throw new DiskBaseFormatException("the header or key table is truncated, or the stream is not seekable.");
        }

        using var input = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        stream.Position = 0;
        if (!input.ReadBytes(DiskBaseFormat.Signature.Length).AsSpan().SequenceEqual(DiskBaseFormat.Signature))
        {
            throw new DiskBaseFormatException("the QuickDir signature is missing.");
        }

        var version = input.ReadInt32();
        var required = input.ReadInt32();
        if (version != DiskBaseFormat.Version || required != DiskBaseFormat.Version)
        {
            throw new DiskBaseFormatException($"database versions 0x{version:X4}/0x{required:X4} are not supported.");
        }

        stream.Position = DiskBaseFormat.KeyOffset;
        var keySize = input.ReadInt32();
        input.ReadInt32();
        var count = input.ReadInt32();
        var valid = input.ReadInt32();
        var deleted = input.ReadInt32();
        if (keySize != 10000 || count < 0 || count > keySize || valid < 0 || valid > count || deleted < 0 || deleted > count)
        {
            throw new DiskBaseFormatException("the key table header is invalid.");
        }
    }

    internal static IReadOnlyList<Dictionary<string, object?>> Read(Stream source, int codePage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateHeader(source);
        var parser = new DiskBaseReader(source, DiskBaseFormat.GetEncoding(codePage), cancellationToken);
        return parser.ReadCatalog();
    }

    private IReadOnlyList<Dictionary<string, object?>> ReadCatalog()
    {
        source.Position = DiskBaseFormat.KeyOffset + 8;
        var count = reader.ReadInt32();
        source.Position = DiskBaseFormat.KeyOffset + 24;
        var keys = new List<uint>();
        var seen = new HashSet<uint>();
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = reader.ReadUInt16();
            var offset = reader.ReadUInt32();
            if ((attributes & 1) != 0 || offset == 0)
            {
                continue;
            }

            if (!seen.Add(offset))
            {
                throw new DiskBaseFormatException("multiple active keys reference the same tree.");
            }

            keys.Add(offset);
        }

        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadTree(key);
        }

        foreach (var (offset, parentId, directoryOffset) in fileLists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadFileList(offset, parentId, directoryOffset);
        }

        foreach (var (offset, properties) in descriptions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = BeginRecord(offset, 12, versioned: false, minimumSize: 15);
            if (end - source.Position - 6 > 65000)
            {
                throw new DiskBaseFormatException("a description exceeds 65000 bytes.");
            }

            source.Position += 6;
            properties["Description"] = Decode(ReadBytes((int)(end - source.Position), end));
        }

        return rows;
    }

    private void ReadTree(uint offset)
    {
        var end = BeginRecord(offset, 1, versioned: true, minimumSize: 9 + DiskBaseFormat.TreeInfoSize + 44);
        var treeAttr = ReadUInt32(end);
        var scanDate = ReadUInt32(end);
        var dirs = ReadUInt32(end);
        var files = ReadUInt32(end);
        var archives = ReadUInt32(end);
        var archiveDirs = ReadUInt32(end);
        var archiveFiles = ReadUInt32(end);
        Skip(12, end);
        var capacityKb = ReadUInt32(end);
        var freeKb = ReadUInt32(end);
        var sectorsPerCluster = ReadUInt32(end);
        var bytesPerSector = ReadUInt32(end);
        var freeClusters = ReadUInt32(end);
        var totalClusters = ReadUInt32(end);
        var fileSystem = FixedString(32, end);
        var fileSystemFlags = ReadUInt32(end);
        Skip(8, end);
        var originalDrive = Decode(ReadBytes(4, end)).TrimEnd('\0');
        Skip(4, end);
        var name = FixedString(256, end);
        var volumeLabel = FixedString(256, end);
        var originPath = FixedString(256, end);
        Skip(256, end);
        var physical = ReadInt64(end);
        var archived = ReadInt64(end);
        var data = ReadInt64(end);
        Skip(16, end);
        if ((treeAttr & 1) != 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DiskBaseFormatException("an active disk has an empty name.");
        }

        var mediaId = $"media:diskbase:{offset}";
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DiskBaseVolumeLabel"] = volumeLabel,
            ["DiskBaseSourcePath"] = originPath,
            ["DiskBaseOriginalDrive"] = originalDrive,
            ["DiskBaseCapacityBytes"] = (capacityKb * 1024L).ToString(CultureInfo.InvariantCulture),
            ["DiskBaseFreeBytes"] = (freeKb * 1024L).ToString(CultureInfo.InvariantCulture),
            ["DiskBasePhysicalBytes"] = physical.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseArchiveBytes"] = archived.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseDataBytes"] = data.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseDirectoryCount"] = dirs.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseFileCount"] = files.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseArchiveCount"] = archives.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseArchiveDirectoryCount"] = archiveDirs.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseArchiveFileCount"] = archiveFiles.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseFileSystem"] = fileSystem,
            ["DiskBaseFileSystemFlags"] = fileSystemFlags.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseSectorsPerCluster"] = sectorsPerCluster.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseBytesPerSector"] = bytesPerSector.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseFreeClusters"] = freeClusters.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseTotalClusters"] = totalClusters.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseCodePage"] = encoding.CodePage.ToString(CultureInfo.InvariantCulture)
        };
        AddTime(properties, "DiskBaseScanDate", scanDate);
        AddRow(mediaId, null, "media", name, 0, properties);

        var stack = new Stack<(string ParentId, int Remaining, bool Root)>();
        stack.Push((mediaId, 1, true));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stack.Count > DiskBaseFormat.MaximumDepth)
            {
                throw new DiskBaseFormatException("the tree exceeds the supported depth.");
            }

            var (parentId, remaining, root) = stack.Pop();
            if (remaining > 1)
            {
                stack.Push((parentId, remaining - 1, false));
            }

            var nodeOffset = source.Position;
            var children = ReadInt16(end);
            var marker = ReadInt16(end);
            if (children < 0 || marker != unchecked((short)-children) || children > (end - source.Position) / 44)
            {
                throw new DiskBaseFormatException("a directory count or record version is invalid.");
            }

            var description = ReadUInt32(end);
            var time = ReadUInt32(end);
            var size = ReadUInt32(end);
            var attributes = ReadUInt16(end);
            var extension = FixedString(5, end);
            var list = ReadUInt32(end);
            var fileCount = ReadInt32(end);
            var totalSize = ReadInt64(end);
            Skip(4, end);
            var directoryName = ShortString(end) + extension;
            if (fileCount < 0 || totalSize < 0)
            {
                throw new DiskBaseFormatException("a directory file count or total size is invalid.");
            }

            var directoryProperties = EntryProperties(attributes, time);
            if (root)
            {
                foreach (var (key, value) in directoryProperties)
                {
                    properties[key] = value;
                }

                directoryProperties = properties;
            }

            directoryProperties["DiskBaseFilesInFolder"] = fileCount.ToString(CultureInfo.InvariantCulture);
            directoryProperties["DiskBaseFolderFilesBytes"] = totalSize.ToString(CultureInfo.InvariantCulture);
            var id = root ? mediaId : $"folder:diskbase:{offset}:{nodeOffset}";
            if (!root)
            {
                if (string.IsNullOrWhiteSpace(directoryName))
                {
                    throw new DiskBaseFormatException("a directory has an empty name.");
                }

                AddRow(id, parentId, "folder", directoryName, size, directoryProperties);
            }

            if (description != 0)
            {
                descriptions.Add((description, directoryProperties));
            }

            if (list != 0)
            {
                fileLists.Add((list, id, nodeOffset));
            }

            if (children > 0)
            {
                stack.Push((id, children, false));
            }
        }

        if (source.Position != end)
        {
            throw new DiskBaseFormatException("the tree length does not match its directory records.");
        }
    }

    private void ReadFileList(uint offset, string parentId, long directoryOffset)
    {
        var end = BeginRecord(offset, 2, versioned: false, minimumSize: 13);
        var count = ReadInt32(end);
        if (count < 0 || count > (end - source.Position) / 20 || count > DiskBaseFormat.MaximumNodes - nodeCount)
        {
            throw new DiskBaseFormatException("the file-list count is invalid.");
        }

        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var itemOffset = source.Position;
            var description = ReadUInt32(end);
            var time = ReadUInt32(end);
            var size = ReadUInt32(end);
            var attributes = ReadUInt16(end);
            var extension = FixedString(5, end);
            var name = ShortString(end) + extension;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DiskBaseFormatException("a file has an empty name.");
            }

            var properties = EntryProperties(attributes, time);
            AddRow($"file:diskbase:{directoryOffset}:{itemOffset}", parentId, "file", name, size, properties);
            if (description != 0)
            {
                descriptions.Add((description, properties));
            }
        }

        if (source.Position != end)
        {
            throw new DiskBaseFormatException("the file-list length does not match its records.");
        }
    }

    private void AddRow(string id, string? parentId, string kind, string name, long size, Dictionary<string, object?> properties)
    {
        if (++nodeCount > DiskBaseFormat.MaximumNodes)
        {
            throw new DiskBaseFormatException("the catalog contains too many entries.");
        }

        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = id,
            ["parentId"] = parentId,
            ["kind"] = kind,
            ["name"] = name,
            ["sizeBytes"] = size,
            ["properties"] = properties
        });
    }

    private static Dictionary<string, object?> EntryProperties(ushort attributes, uint time)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["DiskBaseAttributes"] = attributes.ToString(CultureInfo.InvariantCulture),
            ["DiskBaseArchive"] = (attributes & 0x100) != 0
        };
        AddTime(properties, "DateModified", time);
        return properties;
    }

    private static void AddTime(Dictionary<string, object?> properties, string key, uint value)
    {
        var timestamp = DiskBaseFormat.DosTimestamp(value);
        if (timestamp is not null)
        {
            properties[key] = timestamp;
        }
    }

    private long BeginRecord(uint offset, byte type, bool versioned, int minimumSize)
    {
        if (offset < DiskBaseFormat.DataOffset || (long)offset + 9 > source.Length)
        {
            throw new DiskBaseFormatException("a record offset is outside the data area.");
        }

        source.Position = offset;
        var size = reader.ReadUInt32();
        var inverse = reader.ReadUInt32();
        var recordType = reader.ReadByte();
        var expected = versioned ? unchecked(0u - size) : ~size;
        if (recordType != type || inverse != expected || size < minimumSize || (long)offset + size > source.Length)
        {
            throw new DiskBaseFormatException("a record has an invalid type, version, size, or length marker.");
        }

        return (long)offset + size;
    }

    private string FixedString(int size, long end)
    {
        var bytes = ReadBytes(size, end);
        if (bytes[0] >= size)
        {
            throw new DiskBaseFormatException("a fixed string has an invalid length.");
        }

        return Decode(bytes.AsSpan(1, bytes[0]).ToArray());
    }

    private string ShortString(long end)
    {
        Ensure(1, end);
        var length = reader.ReadByte();
        return Decode(ReadBytes(length, end));
    }

    private string Decode(byte[] bytes) => DiskBaseFormat.Decode(bytes, encoding);

    private byte[] ReadBytes(int count, long end)
    {
        Ensure(count, end);
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new DiskBaseFormatException("a record is truncated.");
        }

        return bytes;
    }

    private void Skip(int count, long end)
    {
        Ensure(count, end);
        source.Position += count;
    }

    private ushort ReadUInt16(long end) { Ensure(2, end); return reader.ReadUInt16(); }
    private short ReadInt16(long end) { Ensure(2, end); return reader.ReadInt16(); }
    private uint ReadUInt32(long end) { Ensure(4, end); return reader.ReadUInt32(); }
    private int ReadInt32(long end) { Ensure(4, end); return reader.ReadInt32(); }
    private long ReadInt64(long end) { Ensure(8, end); return reader.ReadInt64(); }

    private void Ensure(int count, long end)
    {
        if (count < 0 || source.Position > end || count > end - source.Position)
        {
            throw new DiskBaseFormatException("a record is truncated.");
        }
    }
}
