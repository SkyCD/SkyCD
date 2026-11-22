using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Models;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Parsing;

/// <summary>
/// Reads a CD Catalog Expert 9.x container (<c>.zdc</c>) from a stream.
/// </summary>
/// <remarks>
/// Every read is bounded by the remaining input plus configured safety
/// limits, so malformed length prefixes cannot cause unbounded allocation.
/// Chunk chains are walked iteratively with visited-once sets; chunk numbers
/// must be positive, inside the file, and never repeat within a chain.
/// </remarks>
public static class ZdcReader
{
    private const int CancelCheckEveryMembers = 100;

    static ZdcReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Reads and validates a complete container.
    /// </summary>
    public static ZdcDatabase Read(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var data = ReadAllBytesBounded(source, cancellationToken);
        var position = 0;

        var database = ReadHeader(data, ref position);
        var physicalChunks = (data.Length - ZdcFormat.ChunkBaseOffset) / ZdcFormat.ChunkSize;
        var directoryChunks = new HashSet<int>();
        ReadDirectoryTree(data, database, physicalChunks, directoryChunks, cancellationToken);

        var indexChunks = new HashSet<int>();
        var dataChunks = new HashSet<int>();
        var memberIndex = 0;
        foreach (var member in database.Members)
        {
            if (memberIndex % CancelCheckEveryMembers == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ReadMemberPayload(data, member, physicalChunks, directoryChunks, indexChunks, dataChunks, cancellationToken);
            memberIndex++;
        }

        return database;
    }

    private static ZdcDatabase ReadHeader(byte[] data, ref int position)
    {
        Ensure(data.Length >= ZdcFormat.HeaderSize, "container is shorter than the 364-byte header.");
        for (var i = 0; i < ZdcFormat.ContainerMagic.Length; i++)
        {
            if (data[ZdcFormat.ContainerMagicOffset + i] != ZdcFormat.ContainerMagic[i])
            {
                throw new ZdcCorruptException("missing container signature; not a CD Catalog Expert catalog.");
            }
        }

        for (var i = 0; i < ZdcFormat.ProfileMagic.Length; i++)
        {
            if (data[ZdcFormat.ProfileMagicOffset + i] != ZdcFormat.ProfileMagic[i])
            {
                throw new ZdcCorruptException("missing container profile signature.");
            }
        }

        var chunkSize = ReadUInt32(data, ZdcFormat.ChunkSizeOffset);
        if (chunkSize != ZdcFormat.ChunkSize)
        {
            throw new ZdcCorruptException($"unsupported chunk size {chunkSize}; only {ZdcFormat.ChunkSize} is supported.");
        }

        if ((data.Length - ZdcFormat.ChunkBaseOffset) % ZdcFormat.ChunkSize != 0)
        {
            throw new ZdcCorruptException("container length does not align to the chunk grid.");
        }

        var physicalChunks = (data.Length - ZdcFormat.ChunkBaseOffset) / ZdcFormat.ChunkSize;
        var declaredChunks = ReadUInt32(data, ZdcFormat.ChunkCountOffset);
        if (declaredChunks != (uint)physicalChunks)
        {
            throw new ZdcCorruptException("declared chunk count disagrees with the container length.");
        }

        var database = new ZdcDatabase
        {
            Csc = ReadUInt32(data, ZdcFormat.CscOffset),
            Oof = ReadUInt32(data, ZdcFormat.OofOffset)
        };
        for (var i = 0; i < database.ProfileValues.Length; i++)
        {
            database.ProfileValues[i] = ReadUInt32(data, ZdcFormat.ProfileValuesOffset + (i * 4));
        }

        database.ReservedProfile = data[ZdcFormat.ReservedProfileOffset..(ZdcFormat.ReservedProfileOffset + ZdcFormat.ReservedProfileSize)];
        database.ContainerKey = data[ZdcFormat.ContainerKeyOffset..(ZdcFormat.ContainerKeyOffset + ZdcFormat.ContainerKeySize)];
        if (database.ContainerKey.Any(b => b != 0))
        {
            throw new ZdcEncryptionNotSupportedException("container key is present.");
        }

        var firstTreeChunk = ReadInt32(data, ZdcFormat.FirstDirectoryChunkOffset);
        var entryCount = ReadUInt32(data, ZdcFormat.EntryCountOffset);
        if (entryCount > ZdcFormat.MaxMemberCount)
        {
            throw new ZdcCorruptException($"member count {entryCount} exceeds the safety limit.");
        }

        database.PendingFirstTreeChunk = firstTreeChunk;
        database.PendingEntryCount = (int)entryCount;
        position = ZdcFormat.HeaderSize;
        return database;
    }

    private static void ReadDirectoryTree(
        byte[] data,
        ZdcDatabase database,
        int physicalChunks,
        HashSet<int> directoryChunks,
        CancellationToken cancellationToken)
    {
        var remaining = database.PendingEntryCount;
        if (remaining == 0)
        {
            database.PendingFirstTreeChunk = 0;
            database.PendingEntryCount = 0;
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chunk = database.PendingFirstTreeChunk;
        var visited = new HashSet<int>();
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk <= 0)
            {
                throw new ZdcCorruptException("directory tree ends before all declared members were read.");
            }

            EnsureChunk(chunk, physicalChunks, "directory-tree chunk");
            if (!visited.Add(chunk) || !directoryChunks.Add(chunk))
            {
                throw new ZdcCorruptException("directory-tree chunk visited twice.");
            }

            var chunkOffset = ChunkOffset(chunk);
            var next = ReadInt32(data, chunkOffset);
            var storedChecksum = ReadUInt32(data, chunkOffset + 0x04);
            var actualChecksum = ZdcChecksums.XorWords(data, chunkOffset + ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize);
            if (storedChecksum != actualChecksum)
            {
                throw new ZdcCorruptException("directory-tree chunk fails checksum validation.");
            }

            var reserved = new uint[6];
            for (var i = 0; i < reserved.Length; i++)
            {
                reserved[i] = ReadUInt32(data, chunkOffset + 0x08 + (i * 4));
            }

            database.TreeReserved.Add(reserved);

            var inChunk = Math.Min(remaining, ZdcFormat.MembersPerTreeChunk);
            for (var i = 0; i < inChunk; i++)
            {
                database.Members.Add(ReadMemberRecord(data, chunkOffset + ZdcFormat.ChunkHeaderSize + (i * ZdcFormat.MemberRecordSize), names));
            }

            var unusedOffset = chunkOffset + ZdcFormat.ChunkHeaderSize + (inChunk * ZdcFormat.MemberRecordSize);
            for (var i = unusedOffset; i < chunkOffset + ZdcFormat.ChunkSize; i++)
            {
                if (data[i] != 0)
                {
                    throw new ZdcCorruptException("directory-tree chunk has non-zero unused bytes.");
                }
            }

            remaining -= inChunk;
            chunk = next;
        }

        database.PendingFirstTreeChunk = 0;
        database.PendingEntryCount = 0;
    }

    private static ZdcMember ReadMemberRecord(byte[] data, int offset, HashSet<string> names)
    {
        var member = new ZdcMember
        {
            FirstFileIndexChunk = ReadInt32(data, offset + 0x000),
            StoredLength = ReadUInt64(data, offset + 0x004),
            TimestampA = ReadUInt64(data, offset + 0x00C),
            TimestampB = ReadUInt64(data, offset + 0x014),
            TimestampC = ReadUInt64(data, offset + 0x01C),
            FileType = ReadUInt32(data, offset + 0x024),
            ParentEntry = ReadInt32(data, offset + 0x028)
        };

        if (member.StoredLength > (ulong)ZdcFormat.MaxStoredBytes)
        {
            throw new ZdcCorruptException("member stored length exceeds the safety limit.");
        }

        if (ReadUInt32(data, offset + 0x02C) != 0)
        {
            throw new ZdcCorruptException("member reserved field is not zero.");
        }

        member.FileKey = data[(offset + 0x030)..(offset + 0x050)];
        if (member.FileKey.Any(b => b != 0))
        {
            throw new ZdcEncryptionNotSupportedException($"member file key is present for '{DescribeName(data, offset)}'.");
        }

        member.Reserved = data[(offset + 0x050)..(offset + 0x0DC)];
        member.EntryType = ReadUInt32(data, offset + 0x0DC);
        member.FileNameBytes = ReadMemberName(data, offset + 0x0E0, names);
        member.FileName = Encoding.ASCII.GetString(member.FileNameBytes);
        return member;
    }

    private static byte[] ReadMemberName(byte[] data, int offset, HashSet<string> names)
    {
        var end = offset;
        while (end < offset + ZdcFormat.MemberNameFieldSize && data[end] != 0)
        {
            if (data[end] > 0x7F)
            {
                throw new ZdcCorruptException("member name is not ASCII.");
            }

            end++;
        }

        if (end >= offset + ZdcFormat.MemberNameFieldSize)
        {
            throw new ZdcCorruptException("member name has no NUL terminator.");
        }

        var length = end - offset;
        var name = Encoding.ASCII.GetString(data, offset, length);
        if (name.Contains('/') || name.Contains('\\'))
        {
            throw new ZdcCorruptException($"member name '{name}' contains a path separator.");
        }

        if (name == "..")
        {
            throw new ZdcCorruptException("member name '..' is not allowed.");
        }

        if (name.Length >= 2 && name[1] == ':' && char.IsLetter(name[0]))
        {
            throw new ZdcCorruptException($"member name '{name}' is an absolute path.");
        }

        if (!names.Add(name))
        {
            throw new ZdcCorruptException($"duplicate member name '{name}'.");
        }

        var raw = new byte[length];
        Buffer.BlockCopy(data, offset, raw, 0, length);
        return raw;
    }

    private static string DescribeName(byte[] data, int offset)
    {
        var end = offset + 0x0E0;
        while (end < offset + 0x0E0 + ZdcFormat.MemberNameFieldSize && data[end] != 0 && data[end] <= 0x7F)
        {
            end++;
        }

        return Encoding.ASCII.GetString(data, offset + 0x0E0, Math.Min(end - (offset + 0x0E0), ZdcFormat.MemberNameFieldSize));
    }

    private static void ReadMemberPayload(
        byte[] data,
        ZdcMember member,
        int physicalChunks,
        HashSet<int> directoryChunks,
        HashSet<int> indexChunks,
        HashSet<int> dataChunks,
        CancellationToken cancellationToken)
    {
        if (member.StoredLength == 0)
        {
            if (member.FirstFileIndexChunk != -1)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' declares chained data but zero stored length.");
            }

            member.Payload = [];
            return;
        }

        if (member.FirstFileIndexChunk == -1)
        {
            throw new ZdcCorruptException($"member '{member.FileName}' has no file-index chain for its stored data.");
        }

        var references = new List<int>();
        var chunk = member.FirstFileIndexChunk;
        var visited = new HashSet<int>();
        while (chunk != -1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (chunk <= 0)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' has an invalid file-index chunk reference.");
            }

            EnsureChunk(chunk, physicalChunks, $"member '{member.FileName}' file-index chunk");
            if (!visited.Add(chunk))
            {
                throw new ZdcCorruptException($"member '{member.FileName}' revisits a file-index chunk.");
            }

            if (!indexChunks.Add(chunk))
            {
                throw new ZdcCorruptException($"member '{member.FileName}' shares a file-index chunk with another member.");
            }

            if (directoryChunks.Contains(chunk))
            {
                throw new ZdcCorruptException($"member '{member.FileName}' file-index chunk overlaps the directory tree.");
            }

            var chunkOffset = ChunkOffset(chunk);
            var reserved = new uint[7];
            for (var i = 0; i < reserved.Length; i++)
            {
                reserved[i] = ReadUInt32(data, chunkOffset + 0x04 + (i * 4));
            }

            member.IndexReserved.Add(reserved);

            for (var i = 0; i < ZdcFormat.ReferencesPerIndexChunk; i++)
            {
                references.Add(ReadInt32(data, chunkOffset + ZdcFormat.ChunkHeaderSize + (i * 4)));
            }

            chunk = ReadInt32(data, chunkOffset);
        }

        var storedLength = (int)member.StoredLength;
        var concatenated = new List<byte>(storedLength + ZdcFormat.DataAreaSize);
        foreach (var reference in references)
        {
            if (concatenated.Count >= storedLength)
            {
                break;
            }

            if (reference <= 0)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            EnsureChunk(reference, physicalChunks, $"member '{member.FileName}' file-data chunk");
            if (directoryChunks.Contains(reference) || indexChunks.Contains(reference))
            {
                throw new ZdcCorruptException($"member '{member.FileName}' file-data chunk overlaps directory or index chunks.");
            }

            if (!dataChunks.Add(reference))
            {
                throw new ZdcCorruptException($"member '{member.FileName}' references a file-data chunk twice.");
            }

            var chunkOffset = ChunkOffset(reference);
            var header = new ZdcDataChunkHeader(
                ReadInt32(data, chunkOffset),
                ReadUInt32(data, chunkOffset + 0x08),
                data[(chunkOffset + 0x0C)..(chunkOffset + 0x20)]);
            if ((header.Flags & ZdcFormat.EncryptedDataFlag) != 0)
            {
                throw new ZdcEncryptionNotSupportedException($"member '{member.FileName}' is encrypted.");
            }

            if (header.Flags != ZdcFormat.PlainDataFlags)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' has unsupported file-data flags 0x{header.Flags:X8}.");
            }

            var storedChecksum = ReadUInt32(data, chunkOffset + 0x04);
            var actualChecksum = ZdcChecksums.XorWords(data, chunkOffset + ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize);
            if (storedChecksum != actualChecksum)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' file-data chunk fails checksum validation.");
            }

            member.DataHeaders.Add(header);
            concatenated.AddRange(new ArraySegment<byte>(data, chunkOffset + ZdcFormat.ChunkHeaderSize, ZdcFormat.DataAreaSize));
        }

        if (concatenated.Count < storedLength)
        {
            throw new ZdcCorruptException($"member '{member.FileName}' stored data is truncated.");
        }

        for (var i = storedLength; i < concatenated.Count; i++)
        {
            if (concatenated[i] != 0)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' has non-zero padding after the stored bytes.");
            }
        }

        var stored = concatenated.GetRange(0, storedLength).ToArray();
        member.AacsProfile = ReadUInt32(stored, 0x04);
        member.Payload = ZdcAacsCodec.Unwrap(stored, member.FileName);
        ValidatePayload(member);
    }

    private static void ValidatePayload(ZdcMember member)
    {
        if (member.Payload.Length == 0)
        {
            return;
        }

        if (member.FileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };
                using var text = new MemoryStream(member.Payload, writable: false);
                using var reader = XmlReader.Create(text, settings);
                while (reader.Read())
                {
                }
            }
            catch (XmlException exception)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' is not well-formed XML: {exception.Message}");
            }
        }
        else if (member.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            member.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            if (member.Payload.Length < 4 ||
                member.Payload[0] != 0xFF || member.Payload[1] != 0xD8 ||
                member.Payload[^2] != 0xFF || member.Payload[^1] != 0xD9)
            {
                throw new ZdcCorruptException($"member '{member.FileName}' is not a valid JPEG payload.");
            }
        }
    }

    private static void EnsureChunk(int chunk, int physicalChunks, string what)
    {
        if (chunk < 1 || chunk >= physicalChunks)
        {
            throw new ZdcCorruptException($"{what} {chunk} is outside the container.");
        }
    }

    private static int ChunkOffset(int chunk) =>
        ZdcFormat.ChunkBaseOffset + (chunk * ZdcFormat.ChunkSize);

    private static void Ensure(bool condition, string detail)
    {
        if (!condition)
        {
            throw new ZdcCorruptException(detail);
        }
    }

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static ulong ReadUInt64(byte[] data, int offset)
    {
        uint low = ReadUInt32(data, offset);
        uint high = ReadUInt32(data, offset + 4);
        return low | ((ulong)high << 32);
    }

    private static byte[] ReadAllBytesBounded(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            if (source.Length - source.Position > ZdcFormat.MaxContainerBytes)
            {
                throw new ZdcCorruptException("container size exceeds the safety limit.");
            }
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        var total = 0L;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > ZdcFormat.MaxContainerBytes)
            {
                throw new ZdcCorruptException("container size exceeds the safety limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
