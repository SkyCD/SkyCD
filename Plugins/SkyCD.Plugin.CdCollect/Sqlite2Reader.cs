using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.CdCollect.Exceptions;

namespace SkyCD.Plugin.CdCollect;

/// <summary>
/// Read-only SQLite 2.8 table b-trees. Format facts: SQLite 2.8.17 btree.c,
/// pager.h and vdbe.c (public domain). No SQL or stored executable is evaluated.
/// </summary>
internal sealed class Sqlite2Reader
{
    internal static ReadOnlySpan<byte> Signature => "** This file contains an SQLite 2.1 database **\0"u8;
    private const int PageSize = 1024;
    private const int LocalPayload = 236;
    private readonly byte[] data;
    private readonly bool bigEndian;
    private readonly CancellationToken token;
    private readonly HashSet<uint> usedPages = [];
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal Sqlite2Reader(Stream source, CancellationToken token)
    {
        this.token = token;
        token.ThrowIfCancellationRequested();
        if (source.CanSeek) source.Position = 0;
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            if (copy.Length + read > 256L * 1024 * 1024)
                throw new CdCollectSqlite2VersionException("catalog exceeds the 256 MiB safety limit");
            copy.Write(buffer, 0, read);
        }
        data = copy.ToArray();
        if (!data.AsSpan().StartsWith(Signature))
            throw new CdCollectInvalidFormatException("Expected the SQLite 2 signature in a legacy CDCollect cds.db catalog.");
        if (data.Length < 2 * PageSize || data.Length % PageSize != 0)
            throw Corrupt("incomplete 1024-byte pages");
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48, 4));
        if (magic != 0xdae37528 && magic != 0x2875e3da) throw Corrupt("invalid byte-order marker");
        bigEndian = magic == 0x2875e3da;
        var format = U32(data, 64);
        if (format is < 2 or > 4)
            throw new CdCollectSqlite2VersionException($"SQLite storage format {format}");
    }

    internal List<Sqlite2Row> ReadTable(uint root)
    {
        var result = new List<Sqlite2Row>();
        Visit(root, 0);
        return result;

        void Visit(uint pageNumber, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth > 64) throw Corrupt("b-tree exceeds 64 levels");
            var page = Page(pageNumber);
            var occupied = new bool[PageSize];
            Array.Fill(occupied, true, 0, 8);
            var offset = (int)U16(page, 4);
            while (offset != 0)
            {
                token.ThrowIfCancellationRequested();
                if (offset < 8 || offset % 4 != 0 || offset > PageSize - 12)
                    throw Corrupt("invalid cell offset");
                var keySize = U16(page, offset + 4) + page[offset + 8] * 65536;
                var recordSize = U16(page, offset + 10) + page[offset + 9] * 65536;
                if (keySize != 4 || recordSize == 0 || recordSize > 16 * 1024 * 1024)
                    throw Corrupt("invalid table key or record length");
                var total = keySize + recordSize;
                var local = Math.Min(total, LocalPayload);
                var cellSize = 12 + Math.Max(4, (local + 3) & ~3) + (total > LocalPayload ? 4 : 0);
                if (offset + cellSize > PageSize) throw Corrupt("cell extends beyond page");
                for (var i = offset; i < offset + cellSize; i++)
                {
                    if (occupied[i]) throw Corrupt("overlapping or cyclic cells");
                    occupied[i] = true;
                }
                var left = U32(page, offset);
                if (left != 0) Visit(left, depth + 1);
                var payload = new byte[total];
                Array.Copy(page, offset + 12, payload, 0, local);
                if (total > local)
                {
                    var next = U32(page, offset + 12 + LocalPayload);
                    var copied = local;
                    while (copied < total)
                    {
                        token.ThrowIfCancellationRequested();
                        var overflow = Page(next);
                        var count = Math.Min(total - copied, PageSize - 4);
                        Array.Copy(overflow, 4, payload, copied, count);
                        copied += count;
                        next = U32(overflow, 0);
                    }
                    if (next != 0) throw Corrupt("overflow chain exceeds record length");
                }
                var rowId = unchecked((int)(BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(0, 4)) ^ 0x80000000));
                if (result.Count >= 1_000_000) throw Corrupt("table exceeds one million records");
                result.Add(new Sqlite2Row(rowId, Decode(payload.AsSpan(4))));
                offset = U16(page, offset + 6);
            }
            var right = U32(page, 0);
            if (right != 0) Visit(right, depth + 1);
        }
    }

    private static string?[] Decode(ReadOnlySpan<byte> record)
    {
        var width = record.Length < 256 ? 1 : record.Length < 65536 ? 2 : 3;
        var header = Offset(record, 0, width);
        if (header < width || header > record.Length || header % width != 0)
            throw Corrupt("invalid record header");
        var count = header / width - 1;
        if (count > 128) throw Corrupt("too many record fields");
        var values = new string?[count];
        var start = header;
        for (var i = 0; i < count; i++)
        {
            var end = Offset(record, (i + 1) * width, width);
            if (end < start || end > record.Length) throw Corrupt("invalid field bounds");
            if (end > start)
            {
                if (record[end - 1] != 0) throw Corrupt("unterminated text field");
                try { values[i] = Utf8.GetString(record[start..(end - 1)]); }
                catch (DecoderFallbackException) { throw Corrupt("text is not UTF-8"); }
            }
            start = end;
        }
        if (start != record.Length) throw Corrupt("record length does not match fields");
        return values;
    }

    private static int Offset(ReadOnlySpan<byte> record, int offset, int width)
    {
        if (offset < 0 || offset + width > record.Length) throw Corrupt("truncated field index");
        var value = 0;
        for (var i = 0; i < width; i++) value |= record[offset + i] << (8 * i);
        return value;
    }

    private byte[] Page(uint number)
    {
        if (number < 2 || number > data.Length / PageSize || !usedPages.Add(number))
            throw Corrupt("out-of-range, reused, or cyclic page reference");
        var page = new byte[PageSize];
        Array.Copy(data, ((long)number - 1) * PageSize, page, 0, PageSize);
        return page;
    }

    private ushort U16(byte[] bytes, int offset) => bigEndian
        ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2))
        : BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
    private uint U32(byte[] bytes, int offset) => bigEndian
        ? BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4))
        : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    private static CdCollectSqlite2CorruptException Corrupt(string reason) => new(reason);
}

internal sealed record Sqlite2Row(int RowId, string?[] Values);
