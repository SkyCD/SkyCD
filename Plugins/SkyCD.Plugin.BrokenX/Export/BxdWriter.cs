using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.BrokenX.Models;
using SkyCD.Plugin.BrokenX.Parsing;

namespace SkyCD.Plugin.BrokenX.Export;

/// <summary>
/// Writes a Broken X Database (archive version <c>0x40</c>) to a stream.
/// </summary>
/// <remarks>
/// Structural flag bits (<c>0x2000</c>/<c>0x4000</c>) are recomputed from the exported tree;
/// all other flag bits, the fixed fields and every property (including unknown descriptors and
/// their order) round-trip verbatim. Traversal is iterative, so deeply nested exports cannot
/// overflow the call stack.
/// </remarks>
public static class BxdWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private const int CancelCheckEveryRecords = 500;

    /// <summary>
    /// Serializes a complete database.
    /// </summary>
    public static void Write(Stream target, BxdDatabase database, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(database);

        using var buffer = new MemoryStream();
        buffer.Write(BxdFormat.Signature, 0, BxdFormat.SignatureLength);
        buffer.WriteByte(BxdFormat.SupportedVersion);
        WritePropertyBlock(buffer, database.RootProperties);

        var records = Flatten(database.Roots);
        WriteUInt32(buffer, (uint)records.Count);

        var written = 0;
        foreach (var (item, isLastSibling) in records)
        {
            if (written % CancelCheckEveryRecords == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var flags = (ushort)(item.Flags & ~BxdFormat.StructuralMask);
            if (item.Children.Count == 0)
            {
                flags |= BxdFormat.NoChildrenFlag;
            }

            if (isLastSibling)
            {
                flags |= BxdFormat.LastSiblingFlag;
            }

            WriteUInt16(buffer, flags);
            WriteBxdString(buffer, item.NameBytes ?? StrictUtf8.GetBytes(item.Name));
            WriteUInt32(buffer, item.Field14);
            WriteUInt32(buffer, item.Field18);
            WritePropertyBlock(buffer, item.Metadata);
            written++;
        }

        buffer.Position = 0;
        buffer.CopyTo(target);
    }

    private static List<(BxdItem Item, bool IsLastSibling)> Flatten(List<BxdItem> roots)
    {
        var records = new List<(BxdItem, bool)>();

        // Track sibling ends explicitly: each pushed child knows whether it is last.
        var ordered = new Stack<(BxdItem Item, bool IsLast)>();
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            ordered.Push((roots[i], i == roots.Count - 1));
        }

        while (ordered.Count > 0)
        {
            var (item, isLast) = ordered.Pop();
            records.Add((item, isLast));
            for (var i = item.Children.Count - 1; i >= 0; i--)
            {
                ordered.Push((item.Children[i], i == item.Children.Count - 1));
            }
        }

        return records;
    }

    private static void WritePropertyBlock(MemoryStream buffer, List<BxdProperty> properties)
    {
        if (properties.Count > 255)
        {
            throw new InvalidOperationException("A property block holds at most 255 properties.");
        }

        buffer.WriteByte((byte)properties.Count);
        foreach (var property in properties)
        {
            buffer.WriteByte(property.Descriptor);
            switch (property.Kind)
            {
                case BxdPropertyKind.Uint32:
                    WriteUInt32(buffer, property.UintValue);
                    break;
                case BxdPropertyKind.Text:
                    WriteBxdString(buffer, property.RawBytes ?? StrictUtf8.GetBytes(property.Text ?? string.Empty));
                    break;
                case BxdPropertyKind.Blob:
                    var blob = property.RawBytes ?? [];
                    WriteUInt32(buffer, (uint)blob.Length);
                    buffer.Write(blob, 0, blob.Length);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown property kind for descriptor 0x{property.Descriptor:X2}.");
            }
        }
    }

    private static void WriteBxdString(MemoryStream buffer, byte[] bytes)
    {
        if (bytes.Length < BxdFormat.ExtendedLengthMarker)
        {
            buffer.WriteByte((byte)bytes.Length);
        }
        else if (bytes.Length < BxdFormat.LongLengthMarker)
        {
            buffer.WriteByte(0xFF);
            WriteUInt16(buffer, (ushort)bytes.Length);
        }
        else
        {
            buffer.WriteByte(0xFF);
            WriteUInt16(buffer, 0xFFFF);
            WriteUInt32(buffer, (uint)bytes.Length);
        }

        buffer.Write(bytes, 0, bytes.Length);
    }

    private static void WriteUInt16(MemoryStream buffer, ushort value)
    {
        buffer.WriteByte((byte)(value & 0xFF));
        buffer.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteUInt32(MemoryStream buffer, uint value)
    {
        buffer.WriteByte((byte)(value & 0xFF));
        buffer.WriteByte((byte)((value >> 8) & 0xFF));
        buffer.WriteByte((byte)((value >> 16) & 0xFF));
        buffer.WriteByte((byte)((value >> 24) & 0xFF));
    }
}
