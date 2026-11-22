using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Models;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Parsing;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Export;

/// <summary>
/// Writes a CD Catalog Expert 9.x container (<c>.zdc</c>) to a stream.
/// </summary>
/// <remarks>
/// <para>
/// The layout is rebuilt deterministically from the member list: chunks
/// <c>0..3</c> are reserved, the directory tree starts at chunk
/// <see cref="ZdcFormat.FirstDirectoryChunk"/>, each member owns its
/// file-index chain, and data chunks follow. Member order is canonical
/// (<c>zdc_index.xml</c>, disc XML by name, covers by name, others by name),
/// so the same database always serializes to the same bytes.
/// </para>
/// <para>
/// Raw reserved fields travel verbatim when the database was produced by
/// <see cref="ZdcReader"/>; fresh databases use the documented profile
/// defaults. Chain links, counts, checksums, and the AACS envelopes are
/// always recomputed. Traversal is iterative.
/// </para>
/// </remarks>
public static class ZdcWriter
{
    private static readonly Encoding StrictAscii = Encoding.GetEncoding(
        "us-ascii",
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    private const int CancelCheckEveryChunks = 500;

    /// <summary>
    /// Serializes a complete container.
    /// </summary>
    public static void Write(Stream target, ZdcDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(database);

        var members = OrderMembers(database.Members);
        var wrapped = new List<byte[]>(members.Count);
        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            wrapped.Add(WrapMember(member));
        }

        var treeChunkCount = Math.Max(1, (members.Count + ZdcFormat.MembersPerTreeChunk - 1) / ZdcFormat.MembersPerTreeChunk);
        var dataChunkCounts = new List<int>(members.Count);
        var indexChunkCounts = new List<int>(members.Count);
        for (var i = 0; i < members.Count; i++)
        {
            var dataChunks = (wrapped[i].Length + ZdcFormat.DataAreaSize - 1) / ZdcFormat.DataAreaSize;
            dataChunkCounts.Add(dataChunks);
            indexChunkCounts.Add(dataChunks == 0 ? 0 : Math.Max(1, (dataChunks + ZdcFormat.ReferencesPerIndexChunk - 1) / ZdcFormat.ReferencesPerIndexChunk));
        }

        var firstTreeChunk = ZdcFormat.FirstDirectoryChunk;
        var firstIndexChunk = firstTreeChunk + treeChunkCount;
        var firstDataChunk = firstIndexChunk + indexChunkCounts.Sum();
        var totalChunks = firstDataChunk + dataChunkCounts.Sum();

        var firstIndexOfMember = new List<int>(members.Count);
        var firstDataOfMember = new List<int>(members.Count);
        var runningIndex = firstIndexChunk;
        var runningData = firstDataChunk;
        for (var i = 0; i < members.Count; i++)
        {
            firstIndexOfMember.Add(indexChunkCounts[i] == 0 ? -1 : runningIndex);
            firstDataOfMember.Add(dataChunkCounts[i] == 0 ? -1 : runningData);
            runningIndex += indexChunkCounts[i];
            runningData += dataChunkCounts[i];
        }

        using var buffer = new MemoryStream();
        WriteHeader(buffer, database, firstTreeChunk, members.Count, totalChunks);

        var treeReserved = database.TreeReserved;
        var written = 0;
        for (var tree = 0; tree < treeChunkCount; tree++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkNumber = firstTreeChunk + tree;
            var next = tree == treeChunkCount - 1 ? -1 : chunkNumber + 1;
            var chunk = new byte[ZdcFormat.ChunkSize];
            WriteInt32(chunk, 0x00, next);
            var preserved = tree < treeReserved.Count ? treeReserved[tree] : null;
            for (var i = 0; i < 6; i++)
            {
                WriteUInt32(chunk, 0x08 + (i * 4), preserved is not null && i < preserved.Length ? preserved[i] : 0u);
            }

            for (var slot = 0; slot < ZdcFormat.MembersPerTreeChunk; slot++)
            {
                var memberIndex = (tree * ZdcFormat.MembersPerTreeChunk) + slot;
                if (memberIndex >= members.Count)
                {
                    break;
                }

                WriteMemberRecord(chunk, ZdcFormat.ChunkHeaderSize + (slot * ZdcFormat.MemberRecordSize), members[memberIndex], wrapped[memberIndex].Length, firstIndexOfMember[memberIndex]);
            }

            WriteUInt32(chunk, 0x04, ZdcChecksums.XorWords(chunk, ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize));
            buffer.Write(chunk, 0, chunk.Length);
            written++;
            if (written % CancelCheckEveryChunks == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteIndexChain(buffer, members[memberIndex], indexChunkCounts[memberIndex], firstIndexOfMember[memberIndex], firstDataOfMember[memberIndex], dataChunkCounts[memberIndex]);
        }

        for (var memberIndex = 0; memberIndex < members.Count; memberIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteDataChunks(buffer, members[memberIndex], wrapped[memberIndex], firstDataOfMember[memberIndex], dataChunkCounts[memberIndex]);
        }

        buffer.Position = 0;
        buffer.CopyTo(target);
    }

    /// <summary>
    /// Canonical member order: the catalog index, disc XML by name, covers by
    /// name, then any other members by name. A missing index is synthesized as
    /// an empty member so every file carries the required index.
    /// </summary>
    public static List<ZdcMember> OrderMembers(List<ZdcMember> members)
    {
        var ordered = members.ToList();
        if (!ordered.Any(m => string.Equals(m.FileName, ZdcFormat.IndexMemberName, StringComparison.OrdinalIgnoreCase)))
        {
            ordered.Add(new ZdcMember { FileName = ZdcFormat.IndexMemberName });
        }

        return ordered
            .OrderBy(m => MemberRank(m.FileName))
            .ThenBy(m => m.FileName, StringComparer.Ordinal)
            .ToList();
    }

    private static int MemberRank(string fileName)
    {
        if (string.Equals(fileName, ZdcFormat.IndexMemberName, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 3;
    }

    private static byte[] WrapMember(ZdcMember member)
    {
        if (member.Payload.Length == 0)
        {
            return [];
        }

        return ZdcAacsCodec.Wrap(member.Payload, member.AacsProfile);
    }

    private static void WriteHeader(MemoryStream buffer, ZdcDatabase database, int firstTreeChunk, int entryCount, int totalChunks)
    {
        var header = new byte[ZdcFormat.ChunkBaseOffset + (ZdcFormat.FirstDirectoryChunk * ZdcFormat.ChunkSize)];
        ZdcFormat.ContainerMagic.CopyTo(header, ZdcFormat.ContainerMagicOffset);
        ZdcFormat.ProfileMagic.CopyTo(header, ZdcFormat.ProfileMagicOffset);
        WriteUInt32(header, ZdcFormat.CscOffset, database.Csc);
        WriteUInt32(header, ZdcFormat.OofOffset, database.Oof);
        WriteUInt32(header, ZdcFormat.ChunkSizeOffset, ZdcFormat.ChunkSize);
        for (var i = 0; i < 5; i++)
        {
            WriteUInt32(header, ZdcFormat.ProfileValuesOffset + (i * 4), i < database.ProfileValues.Length ? database.ProfileValues[i] : 0u);
        }

        var reservedProfile = database.ReservedProfile;
        if (reservedProfile.Length == ZdcFormat.ReservedProfileSize)
        {
            reservedProfile.CopyTo(header, ZdcFormat.ReservedProfileOffset);
        }

        WriteInt32(header, ZdcFormat.FirstDirectoryChunkOffset, firstTreeChunk);
        WriteUInt32(header, ZdcFormat.EntryCountOffset, (uint)entryCount);
        WriteUInt32(header, ZdcFormat.ChunkCountOffset, (uint)totalChunks);

        var containerKey = database.ContainerKey;
        if (containerKey.Length == ZdcFormat.ContainerKeySize)
        {
            if (containerKey.Any(b => b != 0))
            {
                throw new ZdcCorruptException("cannot write an encrypted container.");
            }

            containerKey.CopyTo(header, ZdcFormat.ContainerKeyOffset);
        }

        buffer.Write(header, 0, header.Length);
    }

    private static void WriteMemberRecord(byte[] chunk, int offset, ZdcMember member, int wrappedLength, int firstIndexChunk)
    {
        var nameBytes = EncodeName(member.FileName);
        WriteInt32(chunk, offset + 0x000, wrappedLength == 0 ? -1 : firstIndexChunk);
        WriteUInt64(chunk, offset + 0x004, (ulong)wrappedLength);
        WriteUInt64(chunk, offset + 0x00C, member.TimestampA);
        WriteUInt64(chunk, offset + 0x014, member.TimestampB);
        WriteUInt64(chunk, offset + 0x01C, member.TimestampC);
        WriteUInt32(chunk, offset + 0x024, member.FileType);
        WriteInt32(chunk, offset + 0x028, member.ParentEntry);
        WriteUInt32(chunk, offset + 0x02C, 0);

        var fileKey = member.FileKey;
        if (fileKey.Length == ZdcFormat.MemberKeySize)
        {
            if (fileKey.Any(b => b != 0))
            {
                throw new ZdcCorruptException($"cannot write encrypted member '{member.FileName}'.");
            }

            fileKey.CopyTo(chunk, offset + 0x030);
        }

        if (member.Reserved.Length == ZdcFormat.MemberReservedSize)
        {
            member.Reserved.CopyTo(chunk, offset + 0x050);
        }

        WriteUInt32(chunk, offset + 0x0DC, member.EntryType);
        nameBytes.CopyTo(chunk, offset + 0x0E0);
    }

    private static void WriteIndexChain(
        MemoryStream buffer,
        ZdcMember member,
        int indexChunkCount,
        int firstIndexChunk,
        int firstDataChunk,
        int dataChunkCount)
    {
        for (var index = 0; index < indexChunkCount; index++)
        {
            var chunkNumber = firstIndexChunk + index;
            var chunk = new byte[ZdcFormat.ChunkSize];
            WriteInt32(chunk, 0x00, index == indexChunkCount - 1 ? -1 : chunkNumber + 1);

            var preserved = index < member.IndexReserved.Count ? member.IndexReserved[index] : null;
            for (var i = 0; i < 7; i++)
            {
                WriteUInt32(chunk, 0x04 + (i * 4), preserved is not null && i < preserved.Length ? preserved[i] : 0u);
            }

            for (var slot = 0; slot < ZdcFormat.ReferencesPerIndexChunk; slot++)
            {
                var referenceIndex = (index * ZdcFormat.ReferencesPerIndexChunk) + slot;
                var reference = referenceIndex < dataChunkCount ? firstDataChunk + referenceIndex : 0;
                WriteInt32(chunk, ZdcFormat.ChunkHeaderSize + (slot * 4), reference);
            }

            buffer.Write(chunk, 0, chunk.Length);
        }
    }

    private static void WriteDataChunks(
        MemoryStream buffer,
        ZdcMember member,
        byte[] wrapped,
        int firstDataChunk,
        int dataChunkCount)
    {
        for (var index = 0; index < dataChunkCount; index++)
        {
            var chunk = new byte[ZdcFormat.ChunkSize];
            var preserved = index < member.DataHeaders.Count ? member.DataHeaders[index] : null;
            WriteInt32(chunk, 0x00, preserved?.Link ?? 0);
            WriteUInt32(chunk, 0x08, preserved?.Flags ?? ZdcFormat.PlainDataFlags);

            var reserved = preserved?.Reserved;
            if (reserved is not null && reserved.Length == ZdcFormat.DataHeaderReservedSize)
            {
                reserved.CopyTo(chunk, 0x0C);
            }

            var sliceOffset = index * ZdcFormat.DataAreaSize;
            var sliceLength = Math.Min(ZdcFormat.DataAreaSize, wrapped.Length - sliceOffset);
            Buffer.BlockCopy(wrapped, sliceOffset, chunk, ZdcFormat.ChunkHeaderSize, sliceLength);

            WriteUInt32(chunk, 0x04, ZdcChecksums.XorWords(chunk, ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize));
            buffer.Write(chunk, 0, chunk.Length);
        }
    }

    private static byte[] EncodeName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            throw new ZdcCorruptException("member name is empty.");
        }

        byte[] encoded;
        try
        {
            encoded = StrictAscii.GetBytes(fileName);
        }
        catch (EncoderFallbackException)
        {
            throw new ZdcCorruptException($"member name '{fileName}' is not ASCII.");
        }

        if (encoded.Length > ZdcFormat.MemberNameFieldSize - 1)
        {
            throw new ZdcCorruptException($"member name '{fileName}' exceeds the 287-byte limit.");
        }

        var field = new byte[ZdcFormat.MemberNameFieldSize];
        encoded.CopyTo(field, 0);
        return field;
    }

    private static void WriteInt32(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value & 0xFF);
        data[offset + 1] = (byte)((value >> 8) & 0xFF);
        data[offset + 2] = (byte)((value >> 16) & 0xFF);
        data[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteUInt32(byte[] data, int offset, uint value) =>
        WriteInt32(data, offset, unchecked((int)value));

    private static void WriteUInt64(byte[] data, int offset, ulong value)
    {
        WriteUInt32(data, offset, (uint)(value & 0xFFFFFFFF));
        WriteUInt32(data, offset + 4, (uint)(value >> 32));
    }
}
