using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SkyCD.Plugin.DiskExplorer.Ded;

namespace SkyCD.Plugin.DiskExplorer.Tests.Ded;

internal static class DedTestData
{
    public static DedDatabase BuildSampleDatabase()
    {
        var database = new DedDatabase
        {
            Title = "Sample Archive",
            HeaderFlags = 0x00000020,
            ReservedFlags = 0x00000004,
            CreationFileTime = DedFileTime.FromUtc(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)),
            TableSlotCount = 1024
        };

        var root = new DedCatalogObject
        {
            Kind = DedObjectKind.Database,
            ObjectId = 1,
            Name = DedText.FromAnsi("Catalog"),
            Description = DedText.FromAnsi("Sample database")
        };

        var category = new DedCatalogObject
        {
            Kind = DedObjectKind.CategoryFolder,
            ObjectId = 2,
            ParentId = 1,
            Name = DedText.FromAnsi("Games"),
            Description = DedText.FromAnsi("Category comment"),
            FolderCount = 1
        };

        var volume = new DedCatalogObject
        {
            Kind = DedObjectKind.Volume,
            ObjectId = 3,
            ParentId = 1,
            Name = DedText.FromAnsi("Disc 1"),
            Path = DedText.FromAnsi("D:\\"),
            LogicalSize = 700L * 1024 * 1024,
            AggregateSize = 5L * 1024 * 1024 * 1024,
            FolderCount = 1,
            FileCount = 2,
            CreatedFileTime = DedFileTime.FromUtc(new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc)),
            ModifiedFileTime = DedFileTime.FromUtc(new DateTime(2002, 3, 4, 5, 6, 7, DateTimeKind.Utc))
        };

        var folder = new DedCatalogObject
        {
            Kind = DedObjectKind.Folder,
            ObjectId = 4,
            ParentId = 3,
            Name = DedText.FromAnsi("Setup"),
            Path = DedText.FromAnsi("D:\\Setup"),
            FileCount = 2
        };

        var archive = new DedCatalogObject
        {
            Kind = DedObjectKind.Archive,
            ObjectId = 5,
            ParentId = 4,
            Name = DedText.FromAnsi("setup.zip"),
            Path = DedText.FromAnsi("D:\\Setup\\setup.zip"),
            LogicalSize = 1234,
            AggregateSize = 1234,
            ModifiedFileTime = ulong.MaxValue
        };

        var file = new DedCatalogObject
        {
            Kind = DedObjectKind.File,
            ObjectId = 6,
            ParentId = 4,
            Name = DedText.FromAnsi("Café €.txt"),
            Path = DedText.FromAnsi("D:\\Setup\\Café €.txt"),
            Description = DedText.FromAnsi("Grüße aus Europa"),
            LogicalSize = 4_294_967_296UL + 1024,
            AggregateSize = 4_294_967_296UL + 1024,
            Flags = 0x0000000F,
            CreatedFileTime = DedFileTime.FromUtc(new DateTime(2019, 5, 6, 7, 8, 9, DateTimeKind.Utc)),
            ModifiedFileTime = DedFileTime.FromUtc(new DateTime(2021, 6, 7, 8, 9, 10, DateTimeKind.Utc)),
            AddedFileTime = DedFileTime.FromUtc(new DateTime(2022, 7, 8, 9, 10, 11, DateTimeKind.Utc))
        };

        file.CategoryIds.Add(2);
        file.Extensions.Add(new DedExtensionField(0x0000DEAD, [1, 2, 3, 4, 5]));
        file.Extensions.Add(new DedExtensionField(0x0000BEEF, Encoding.ASCII.GetBytes("opaque")));
        file.Payloads.Add(new DedPayload(DedPayloadKind.StoredContent, Encoding.ASCII.GetBytes("file contents")));
        file.Payloads.Add(new DedPayload(DedPayloadKind.Thumbnail, [0x89, 0x50, 0x4E, 0x47, 1, 2, 3]));
        file.Payloads.Add(new DedPayload(DedPayloadKind.GeneratedDescription, Encoding.UTF8.GetBytes("A generated summary")));
        file.Payloads.Add(new DedPayload(0x00000077, [9, 8, 7, 6]));

        var volume2 = new DedCatalogObject
        {
            Kind = DedObjectKind.Volume,
            ObjectId = 7,
            ParentId = 1,
            Name = DedText.FromAnsi("Disc 2"),
            FolderCount = 0,
            FileCount = 1
        };

        var readme = new DedCatalogObject
        {
            Kind = DedObjectKind.File,
            ObjectId = 8,
            ParentId = 7,
            Name = DedText.FromAnsi("readme.txt"),
            LogicalSize = 55,
            AggregateSize = 55
        };
        readme.Payloads.Add(new DedPayload(DedPayloadKind.StoredContent, Encoding.ASCII.GetBytes("hello")));

        root.ChildIds.AddRange([2, 3, 7]);
        volume.ChildIds.Add(4);
        folder.ChildIds.AddRange([5, 6]);
        volume2.ChildIds.Add(8);

        database.Objects.AddRange([root, category, volume, folder, archive, file, volume2, readme]);
        return database;
    }

    /// <summary>Builds a raw container with explicit table slots and records.</summary>
    public static byte[] BuildContainer(
        IReadOnlyList<byte[]> records,
        uint[] slots,
        uint headerFlags = 0,
        uint populatedCount = 0,
        uint version = DedFormat.CurrentVersion,
        ulong creationFileTime = 0)
    {
        var body = new MemoryStream();
        var headerSize = DedFormat.HeaderSize;
        var tableBytes = slots.Length * 4;
        var offset = (ulong)headerSize + (ulong)tableBytes;
        var offsets = new uint[records.Count];
        for (var i = 0; i < records.Count; i++)
        {
            offsets[i] = (uint)offset;
            offset += (ulong)records[i].Length;
        }

        if (offsets.Length > slots.Length)
        {
            throw new ArgumentException("more records than slots", nameof(records));
        }

        var file = new byte[checked((int)offset)];
        DedFormat.Magic.CopyTo(file);
        WriteUInt32(file, 0x04, version);
        WriteUInt32(file, 0x08, (uint)slots.Length);
        WriteUInt32(file, 0x0C, populatedCount == 0 ? (uint)records.Count : populatedCount);
        WriteUInt32(file, DedFormat.FlagsOffset, headerFlags);
        WriteUInt64(file, DedFormat.CreationTimeOffset, creationFileTime);
        for (var i = 0; i < slots.Length; i++)
        {
            WriteUInt32(file, headerSize + (i * 4), slots[i]);
        }

        for (var i = 0; i < records.Count; i++)
        {
            Array.Copy(records[i], 0, file, offsets[i], records[i].Length);
        }

        return file;
    }

    /// <summary>Builds a raw container with sequential slots for every record.</summary>
    public static byte[] BuildContainer(
        IReadOnlyList<byte[]> records,
        int slotCount,
        uint headerFlags = 0,
        uint version = DedFormat.CurrentVersion)
    {
        var offset = (uint)DedFormat.HeaderSize + ((uint)slotCount * 4);
        var slots = new uint[slotCount];
        for (var i = 0; i < records.Count; i++)
        {
            slots[i] = offset;
            offset += (uint)records[i].Length;
        }

        return BuildContainer(records, slots, headerFlags, (uint)records.Count, version);
    }

    public static byte[] BuildContainer(DedDatabase database, uint[] slots)
    {
        var records = new List<byte[]>();
        foreach (var item in database.Objects)
        {
            records.Add(item.IsUnknownKind ? item.RawRecord! : DedRecordSerializer.Serialize(item));
        }

        return BuildContainer(records, slots, database.HeaderFlags, 0, database.FormatVersion, database.CreationFileTime);
    }

    public static MemoryStream WriteToStream(DedDatabase database)
    {
        var stream = new MemoryStream();
        DedWriter.Write(stream, database);
        stream.Position = 0;
        return stream;
    }

    public static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    public static uint ReadUInt32(byte[] buffer, int offset) =>
        (uint)(buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24));

    public static ulong ReadUInt64(byte[] buffer, int offset) =>
        ReadUInt32(buffer, offset) | ((ulong)ReadUInt32(buffer, offset + 4) << 32);

    public static void WriteUInt64(byte[] buffer, int offset, ulong value)
    {
        WriteUInt32(buffer, offset, (uint)value);
        WriteUInt32(buffer, offset + 4, (uint)(value >> 32));
    }
}
