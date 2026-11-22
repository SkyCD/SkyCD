using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.FileFormat.BrokenX.Exceptions;
using SkyCD.Plugin.FileFormat.BrokenX.Models;

namespace SkyCD.Plugin.FileFormat.BrokenX.Parsing;

/// <summary>
/// Reads a Broken X Database (archive version <c>0x40</c>) from a stream.
/// </summary>
/// <remarks>
/// Every read is bounded by the remaining input length plus configured safety limits, so
/// malformed length prefixes cannot cause unbounded allocation. The tree is rebuilt with an
/// explicit stack (no recursion) and the depth is capped at <see cref="BxdFormat.MaxDepth"/>.
/// </remarks>
public static class BxdReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int CancelCheckEveryRecords = 500;
    private const int MaxDatabaseBytes = 256 * 1024 * 1024;

    /// <summary>
    /// Reads and validates a complete database.
    /// </summary>
    public static BxdDatabase Read(Stream source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var data = ReadAllBytesBounded(source, cancellationToken);
        var position = 0;

        EnsureRemaining(data, position, BxdFormat.SignatureLength + 1, "file header");
        for (var i = 0; i < BxdFormat.SignatureLength; i++)
        {
            if (data[position + i] != BxdFormat.Signature[i])
            {
                throw new BxdCorruptException("missing file signature; not a Broken X database.");
            }
        }

        position += BxdFormat.SignatureLength;
        var version = data[position++];
        if (version != BxdFormat.SupportedVersion)
        {
            throw new BxdUnsupportedVersionException(version);
        }

        var database = new BxdDatabase();
        position = ReadPropertyBlock(data, position, database.RootProperties, "root properties");

        EnsureRemaining(data, position, 4, "item count");
        var itemCount = ReadUInt32(data, ref position);
        if (itemCount > BxdFormat.MaxItemCount)
        {
            throw new BxdCorruptException($"item count {itemCount} exceeds the safety limit.");
        }

        var stack = new List<BxdItem>();
        for (var index = 0; index < itemCount; index++)
        {
            if (index % CancelCheckEveryRecords == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (stack.Count > BxdFormat.MaxDepth)
            {
                throw new BxdCorruptException($"nesting depth exceeds {BxdFormat.MaxDepth} levels.");
            }

            var item = ReadItem(data, ref position, index);
            if (stack.Count == 0)
            {
                database.Roots.Add(item);
            }
            else
            {
                stack[^1].Children.Add(item);
            }

            if ((item.Flags & BxdFormat.NoChildrenFlag) == 0)
            {
                stack.Add(item);
            }
            else
            {
                var flags = item.Flags;
                while ((flags & BxdFormat.LastSiblingFlag) != 0 && stack.Count > 0)
                {
                    var parent = stack[^1];
                    stack.RemoveAt(stack.Count - 1);
                    flags = parent.Flags;
                }
            }
        }

        if (stack.Count > 0)
        {
            throw new BxdCorruptException("unclosed folders at the end of the record stream.");
        }

        if (position != data.Length)
        {
            throw new BxdCorruptException("trailing data after the last item record.");
        }

        return database;
    }

    private static BxdItem ReadItem(byte[] data, ref int position, int index)
    {
        EnsureRemaining(data, position, 2, $"item {index} flags");
        var flags = ReadUInt16(data, ref position);

        var (name, nameBytes) = ReadBxdString(data, ref position, $"item {index} name");

        EnsureRemaining(data, position, 8, $"item {index} fixed fields");
        var field14 = ReadUInt32(data, ref position);
        var field18 = ReadUInt32(data, ref position);

        var item = new BxdItem
        {
            Flags = flags,
            Name = name,
            NameBytes = nameBytes,
            Field14 = field14,
            Field18 = field18
        };
        position = ReadPropertyBlock(data, position, item.Metadata, $"item {index} metadata");

        if ((flags & BxdFormat.ProtectedRecordFlag) != 0)
        {
            throw new BxdEncryptedRecordException(name);
        }

        return item;
    }

    private static int ReadPropertyBlock(byte[] data, int position, List<BxdProperty> target, string what)
    {
        EnsureRemaining(data, position, 1, $"{what} count");
        var count = data[position++];

        for (var i = 0; i < count; i++)
        {
            EnsureRemaining(data, position, 1, $"{what} descriptor");
            var descriptor = data[position++];
            var kind = (descriptor >> 6) & 0x03;
            switch (kind)
            {
                case 0:
                    throw new BxdCorruptException($"{what} has invalid descriptor 0x{descriptor:X2}.");
                case 1:
                    EnsureRemaining(data, position, 4, $"{what} uint32 payload");
                    target.Add(new BxdProperty(descriptor, ReadUInt32(data, ref position)));
                    break;
                case 2:
                    var (text, rawBytes) = ReadBxdString(data, ref position, $"{what} text payload");
                    target.Add(new BxdProperty(descriptor, text, rawBytes));
                    break;
                default:
                    EnsureRemaining(data, position, 4, $"{what} blob length");
                    var blobLength = (int)ReadUInt32(data, ref position);
                    if (blobLength < 0 || blobLength > BxdFormat.MaxBlobBytes)
                    {
                        throw new BxdCorruptException($"{what} blob length exceeds the safety limit.");
                    }

                    EnsureRemaining(data, position, blobLength, $"{what} blob payload");
                    var blob = new byte[blobLength];
                    Buffer.BlockCopy(data, position, blob, 0, blobLength);
                    position += blobLength;
                    target.Add(new BxdProperty(descriptor, blob));
                    break;
            }
        }

        return position;
    }

    private static (string Text, byte[] RawBytes) ReadBxdString(byte[] data, ref int position, string what)
    {
        EnsureRemaining(data, position, 1, $"{what} length");
        var first = data[position++];
        int length;
        if (first < BxdFormat.ExtendedLengthMarker)
        {
            length = first;
        }
        else
        {
            EnsureRemaining(data, position, 2, $"{what} extended length");
            var extended = ReadUInt16(data, ref position);
            if (extended < BxdFormat.LongLengthMarker)
            {
                length = extended;
            }
            else
            {
                EnsureRemaining(data, position, 4, $"{what} long length");
                var huge = ReadUInt32(data, ref position);
                if (huge > int.MaxValue)
                {
                    throw new BxdCorruptException($"{what} length exceeds the safety limit.");
                }

                length = (int)huge;
            }
        }

        if (length < 0 || length > BxdFormat.MaxStringBytes)
        {
            throw new BxdCorruptException($"{what} length exceeds the safety limit.");
        }

        EnsureRemaining(data, position, length, $"{what} bytes");
        var raw = new byte[length];
        Buffer.BlockCopy(data, position, raw, 0, length);
        position += length;

        string text;
        try
        {
            text = StrictUtf8.GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            throw new BxdCorruptException($"{what} is not valid UTF-8.");
        }

        return (text, raw);
    }

    private static void EnsureRemaining(byte[] data, int position, int need, string what)
    {
        if (need < 0 || position < 0 || position > data.Length || need > data.Length - position)
        {
            throw new BxdCorruptException($"truncated {what}.");
        }
    }

    private static ushort ReadUInt16(byte[] data, ref int position)
    {
        var value = (ushort)(data[position] | (data[position + 1] << 8));
        position += 2;
        return value;
    }

    private static uint ReadUInt32(byte[] data, ref int position)
    {
        var value = (uint)(data[position] | (data[position + 1] << 8) | (data[position + 2] << 16) | (data[position + 3] << 24));
        position += 4;
        return value;
    }

    private static byte[] ReadAllBytesBounded(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek)
        {
            if (source.Length - source.Position > MaxDatabaseBytes)
            {
                throw new BxdCorruptException("database size exceeds the safety limit.");
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
            if (total > MaxDatabaseBytes)
            {
                throw new BxdCorruptException("database size exceeds the safety limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
