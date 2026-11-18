using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SkyCD.Plugin.DiskBase.Tests;

internal static class DiskBaseFixture
{
    internal const int KeyOffset = 4937;
    internal const int DataOffset = 64961;
    private static readonly Encoding Encoding = GetEncoding();
    private const uint DosTime = (uint)(((2021 - 1980) << 9 | 2 << 5 | 3) << 16 | (4 << 11 | 5 << 5 | 3));

    internal static (byte[] Bytes, int TreeOffset, int RootListOffset, int DescriptionOffset) Create()
    {
        using var stream = new MemoryStream();
        stream.SetLength(DataOffset);
        using var writer = new BinaryWriter(stream, Encoding, leaveOpen: true);
        stream.Position = 0;
        writer.Write(System.Text.Encoding.ASCII.GetBytes("QuickDir Database "));
        writer.Write(0x0501);
        writer.Write(0x0501);
        stream.Position = DataOffset;
        var description = WriteRecord(writer, 12, false, body =>
        {
            body.Write((ushort)0);
            body.Write(0);
            body.Write(Encoding.GetBytes("résumé"));
        });
        var rootList = WriteFiles(writer, [("note", ".txt", uint.MaxValue, description)]);
        var listA = WriteFiles(writer, [("same", ".txt", 42, 0)]);
        var listB = WriteFiles(writer, [("same", ".txt", 43, 0)]);
        var archiveList = WriteFiles(writer, [("inside", ".bin", 10, 0)]);
        var secondList = WriteFiles(writer, [("other", ".dat", 1, 0)]);
        var tree = WriteTree(writer, "café", "VOL", "C:\\source", new Folder("\\", "", 0x10, rootList, 0,
        [
            new Folder("A", "", 0x10, listA, description, [new Folder("Empty", "", 0x10, 0, 0, [])]),
            new Folder("B", "", 0x10, listB, 0, []),
            new Folder("pack", ".zip", 0x110, archiveList, 0, [])
        ]));
        var tree2 = WriteTree(writer, "Second", "OTHER", "D:\\", new Folder("\\", "", 0x10, secondList, 0, []));
        WriteTree(writer, "STALE", "", "", new Folder("\\", "", 0x10, 0, 0, []));
        stream.Position = KeyOffset;
        writer.Write(10000);
        writer.Write(0);
        writer.Write(3);
        writer.Write(2);
        writer.Write(1);
        writer.Write(0);
        writer.Write((ushort)0);
        writer.Write((uint)tree);
        writer.Write((ushort)0);
        writer.Write((uint)tree2);
        writer.Write((ushort)1);
        writer.Write(uint.MaxValue);
        return (stream.ToArray(), tree, rootList, description);
    }

    private static int WriteTree(BinaryWriter writer, string name, string volume, string path, Folder root)
    {
        return WriteRecord(writer, 1, true, body =>
        {
            var info = new byte[1180];
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(4), DosTime);
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(40), uint.MaxValue);
            BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(44), 1234);
            BinaryPrimitives.WriteInt64LittleEndian(info.AsSpan(1140), 12345678901);
            WriteFixed(info, 116, 256, name);
            WriteFixed(info, 372, 256, volume);
            WriteFixed(info, 628, 256, path);
            body.Write(info);
            WriteFolder(body, root);
        });
    }

    private static void WriteFolder(BinaryWriter writer, Folder folder)
    {
        writer.Write((short)folder.Children.Count);
        writer.Write(unchecked((short)-folder.Children.Count));
        writer.Write((uint)folder.Description);
        writer.Write(DosTime);
        writer.Write((uint)0);
        writer.Write(folder.Attributes);
        WriteFixed(writer, 5, folder.Extension);
        writer.Write((uint)folder.FileList);
        writer.Write(folder.FileList == 0 ? 0 : 1);
        writer.Write((long)42);
        writer.Write(0);
        WriteShort(writer, folder.Name);
        foreach (var child in folder.Children)
        {
            WriteFolder(writer, child);
        }
    }

    private static int WriteFiles(BinaryWriter writer, IReadOnlyList<(string Name, string Extension, uint Size, int Description)> files)
    {
        return WriteRecord(writer, 2, false, body =>
        {
            body.Write(files.Count);
            foreach (var file in files)
            {
                body.Write((uint)file.Description);
                body.Write(DosTime);
                body.Write(file.Size);
                body.Write((ushort)0x20);
                WriteFixed(body, 5, file.Extension);
                WriteShort(body, file.Name);
            }
        });
    }

    private static int WriteRecord(BinaryWriter target, byte type, bool versioned, Action<BinaryWriter> body)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload, Encoding, leaveOpen: true);
        body(writer);
        writer.Flush();
        var offset = checked((int)target.BaseStream.Position);
        var size = checked((uint)payload.Length + 9);
        target.Write(size);
        target.Write(versioned ? unchecked(0u - size) : ~size);
        target.Write(type);
        target.Write(payload.ToArray());
        return offset;
    }

    private static void WriteFixed(BinaryWriter writer, int size, string value)
    {
        var bytes = new byte[size];
        WriteFixed(bytes, 0, size, value);
        writer.Write(bytes);
    }

    private static void WriteFixed(byte[] target, int offset, int size, string value)
    {
        var bytes = Encoding.GetBytes(value);
        if (bytes.Length >= size) throw new ArgumentOutOfRangeException(nameof(value));
        target[offset] = (byte)bytes.Length;
        bytes.CopyTo(target, offset + 1);
    }

    private static void WriteShort(BinaryWriter writer, string value)
    {
        var bytes = Encoding.GetBytes(value);
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private static Encoding GetEncoding()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    private sealed record Folder(string Name, string Extension, ushort Attributes, int FileList, int Description,
        IReadOnlyList<Folder> Children);
}
