using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Parsing;

/// <summary>
/// Parses the documented 36-byte record table. Ordinals must increase; malformed
/// records are skipped with a diagnostic, while well-formed records keep their
/// raw values even when the handler or ranges cannot be interpreted yet.
/// </summary>
public static class AbeMedaNativeEntryReader
{
    public const int MaxWarnings = 1_000;

    public static AbeMedaNativeEntryScan Read(
        Stream source,
        AbeMedaNativeHeader header,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(header);

        var reader = new AbeMedaNativeBinaryReader(source, source.Length);
        var stride = checked((int)header.EntryStride);
        var entries = new List<AbeMedaNativeEntry>((int)Math.Min(header.EntryCount, 4_096u));
        var warnings = new List<string>();
        var skipped = 0;
        var warningsSuppressed = false;
        ulong previousOrdinal = 0;

        for (var index = 0u; index < header.EntryCount; index++)
        {
            if ((index & 0xFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var recordOffset = header.HeaderSize + (long)index * stride;
            var record = reader.ReadBytes(recordOffset, stride);
            var ordinal = BinaryPrimitives.ReadUInt64LittleEndian(
                record.AsSpan(AbeMedaNativeLayout.EntryOrdinalOffset, 8));

            if (ordinal <= previousOrdinal)
            {
                Warn($"record {index} at 0x{recordOffset:X} has non-increasing ordinal {ordinal}.");
                skipped++;
                continue;
            }

            previousOrdinal = ordinal;

            var blobValue = BinaryPrimitives.ReadUInt64LittleEndian(
                record.AsSpan(AbeMedaNativeLayout.EntryBlobValueOffset, 8));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(
                record.AsSpan(AbeMedaNativeLayout.EntryFlagsOffset, 4));
            var rangeA = BinaryPrimitives.ReadUInt32LittleEndian(
                record.AsSpan(AbeMedaNativeLayout.EntryRangeAOffset, 4));
            var rangeB = BinaryPrimitives.ReadUInt32LittleEndian(
                record.AsSpan(AbeMedaNativeLayout.EntryRangeBOffset, 4));

            var handlerBytes = record.AsSpan(AbeMedaNativeLayout.EntryHandlerOffset, AbeMedaNativeLayout.HandlerLength);
            var (handler, handlerIsPrintable) = DecodeHandler(handlerBytes, index, recordOffset);
            var (rangesWithinFile, blobWithinFile) = ValidateRanges(blobValue, rangeA, rangeB, index, recordOffset);

            entries.Add(new AbeMedaNativeEntry(
                RecordOffset: recordOffset,
                Ordinal: ordinal,
                BlobValue: blobValue,
                Flags: flags,
                Handler: handler,
                HandlerIsPrintable: handlerIsPrintable,
                RangeA: rangeA,
                RangeB: rangeB,
                RangesWithinFile: rangesWithinFile));

            if (!blobWithinFile)
            {
                Warn($"record {index} at 0x{recordOffset:X} has a blob value {blobValue} outside the file.");
            }
        }

        return new AbeMedaNativeEntryScan(entries, warnings, skipped);

        (string Handler, bool IsPrintable) DecodeHandler(ReadOnlySpan<byte> bytes, uint index, long recordOffset)
        {
            var length = 0;
            while (length < bytes.Length && bytes[length] != 0)
            {
                length++;
            }

            var printable = true;
            for (var i = 0; i < length; i++)
            {
                if (bytes[i] is < 0x20 or > 0x7E)
                {
                    printable = false;
                    break;
                }
            }

            if (printable)
            {
                return (Encoding.ASCII.GetString(bytes[..length]), true);
            }

            Warn($"record {index} at 0x{recordOffset:X} has a non-text handler.");
            return ("0x" + Convert.ToHexString(bytes), false);
        }

        (bool RangesWithinFile, bool BlobWithinFile) ValidateRanges(
            ulong blobValue, uint rangeA, uint rangeB, uint index, long recordOffset)
        {
            var length = (ulong)source.Length;
            var rangesWithinFile = rangeA < length && rangeB < length;
            if (!rangesWithinFile)
            {
                Warn(
                    $"record {index} at 0x{recordOffset:X} has ranges 0x{rangeA:X8}/0x{rangeB:X8} outside the file.");
            }

            return (rangesWithinFile, blobValue <= length);
        }

        void Warn(string message)
        {
            if (warningsSuppressed)
            {
                return;
            }

            if (warnings.Count >= MaxWarnings)
            {
                warningsSuppressed = true;
                warnings.Add($"Additional warnings were suppressed after {MaxWarnings} entries.");
                return;
            }

            warnings.Add(message);
        }
    }
}
