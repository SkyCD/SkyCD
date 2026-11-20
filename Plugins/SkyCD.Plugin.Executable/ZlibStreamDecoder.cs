using System;
using System.IO;

namespace SkyCD.Plugin.Executable;

/// <summary>
/// Decodes a zlib stream with an explicit output bound.
/// </summary>
/// <remarks>
/// .NET's <see cref="System.IO.Compression.ZLibStream"/> has no maximum-output overload, so a decompression
/// bomb would be expanded into memory before the caller could reject it. The plugin only needs the format
/// icon images actually use — a valid zlib header followed by deflate blocks of stored or Huffman-coded data —
/// so this small decoder enforces the bound while it writes and rejects anything it does not understand
/// instead of guessing.
/// </remarks>
internal static class ZlibStreamDecoder
{
    /// <summary>The longest Huffman code a deflate stream may use.</summary>
    private const int MaximumCodeLength = 15;

    /// <summary>
    /// Inflates <paramref name="data"/> starting at <paramref name="offset"/> and returns at most
    /// <paramref name="maximumBytes"/> bytes. Throws <see cref="InvalidDataException"/> for malformed input and
    /// when the output would exceed the bound.
    /// </summary>
    public static byte[] Decode(ReadOnlySpan<byte> data, int offset, int maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);

        if (offset < 0 || data.Length - offset < 2)
        {
            throw new InvalidDataException("The zlib stream is truncated.");
        }

        var cmf = data[offset];
        var flg = data[offset + 1];
        if ((cmf & 0x0F) != 8)
        {
            throw new InvalidDataException("The zlib stream does not use the deflate method.");
        }

        if ((cmf >> 4) > 7 || ((cmf << 8) | flg) % 31 != 0)
        {
            throw new InvalidDataException("The zlib header is not valid.");
        }

        if ((flg & 0x20) != 0)
        {
            throw new InvalidDataException("Zlib preset dictionaries are not supported.");
        }

        var inflater = new Inflater(data.ToArray(), offset + 2, maximumBytes);
        return inflater.Inflate();
    }

    /// <summary>A single-pass, bounds-checked deflate decoder over a span.</summary>
    private sealed class Inflater
    {

        private static readonly int[] LengthBase =
            [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163,
                195, 227, 258];

        private static readonly int[] LengthExtra =
            [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];

        private static readonly int[] DistanceBase =
            [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049,
                3073, 4097, 6145, 8193, 12289, 16385, 24577];

        private static readonly int[] DistanceExtra =
            [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

        private static readonly int[] CodeLengthOrder =
            [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

        private readonly byte[] input;
        private readonly int end;
        private readonly int maximumBytes;

        private int position;
        private uint bitBuffer;
        private int bitCount;
        private byte[] output;
        private int outputLength;

        public Inflater(byte[] input, int start, int maximumBytes)
        {
            this.input = input;
            end = input.Length;
            this.maximumBytes = maximumBytes;
            position = start;
            output = new byte[Math.Min(Math.Max(input.Length * 4, 1024), maximumBytes)];
        }

        public byte[] Inflate()
        {
            var last = 0;
            while (last == 0)
            {
                last = ReadBits(1);
                switch (ReadBits(2))
                {
                    case 0:
                        ReadStoredBlock();
                        break;
                    case 1:
                        ReadFixedBlock();
                        break;
                    case 2:
                        ReadDynamicBlock();
                        break;
                    default:
                        throw new InvalidDataException("The deflate block type is not valid.");
                }
            }

            return outputLength == output.Length ? output : output[..outputLength];
        }

        private void ReadStoredBlock()
        {
            bitBuffer = 0;
            bitCount = 0;
            if (end - position < 4)
            {
                throw new InvalidDataException("The deflate stream is truncated.");
            }

            var length = input[position] | (input[position + 1] << 8);
            var complement = input[position + 2] | (input[position + 3] << 8);
            position += 4;
            if (length != (~complement & 0xFFFF))
            {
                throw new InvalidDataException("A stored deflate block has an inconsistent length.");
            }

            if (end - position < length)
            {
                throw new InvalidDataException("The deflate stream is truncated.");
            }

            Append(input.AsSpan(position, length));
            position += length;
        }

        private void ReadFixedBlock()
        {
            Span<int> literalLengths = stackalloc int[288];
            for (var index = 0; index < 144; index++)
            {
                literalLengths[index] = 8;
            }

            for (var index = 144; index < 256; index++)
            {
                literalLengths[index] = 9;
            }

            for (var index = 256; index < 280; index++)
            {
                literalLengths[index] = 7;
            }

            for (var index = 280; index < 288; index++)
            {
                literalLengths[index] = 8;
            }

            Span<int> distanceLengths = stackalloc int[30];
            distanceLengths.Fill(5);

            ReadCodes(HuffmanTable.Build(literalLengths), HuffmanTable.Build(distanceLengths));
        }

        private void ReadDynamicBlock()
        {
            var literalCount = ReadBits(5) + 257;
            var distanceCount = ReadBits(5) + 1;
            var codeLengthCount = ReadBits(4) + 4;
            if (literalCount > 286 || distanceCount > 30)
            {
                throw new InvalidDataException("The dynamic deflate header is out of range.");
            }

            Span<int> codeLengthCodeLengths = stackalloc int[19];
            codeLengthCodeLengths.Clear();
            for (var index = 0; index < codeLengthCount; index++)
            {
                codeLengthCodeLengths[CodeLengthOrder[index]] = ReadBits(3);
            }

            var codeLengthTable = HuffmanTable.Build(codeLengthCodeLengths);

            Span<int> lengths = stackalloc int[literalCount + distanceCount];
            lengths.Clear();
            var filled = 0;
            while (filled < lengths.Length)
            {
                var symbol = Decode(codeLengthTable);
                if (symbol < 16)
                {
                    lengths[filled++] = symbol;
                    continue;
                }

                int repeat;
                var value = 0;
                switch (symbol)
                {
                    case 16:
                        if (filled == 0)
                        {
                            throw new InvalidDataException(
                                "A deflate code-length repeat has no previous length.");
                        }

                        value = lengths[filled - 1];
                        repeat = 3 + ReadBits(2);
                        break;
                    case 17:
                        repeat = 3 + ReadBits(3);
                        break;
                    case 18:
                        repeat = 11 + ReadBits(7);
                        break;
                    default:
                        throw new InvalidDataException("The deflate code-length code is not valid.");
                }

                if (filled + repeat > lengths.Length)
                {
                    throw new InvalidDataException("The deflate code lengths overflow their table.");
                }

                lengths.Slice(filled, repeat).Fill(value);
                filled += repeat;
            }

            ReadCodes(HuffmanTable.Build(lengths[..literalCount]),
                HuffmanTable.Build(lengths[literalCount..]));
        }

        private void ReadCodes(HuffmanTable literals, HuffmanTable distances)
        {
            while (true)
            {
                var symbol = Decode(literals);
                if (symbol < 256)
                {
                    Append((byte)symbol);
                    continue;
                }

                if (symbol == 256)
                {
                    return;
                }

                symbol -= 257;
                if (symbol >= LengthBase.Length)
                {
                    throw new InvalidDataException("The deflate length code is not valid.");
                }

                var length = LengthBase[symbol] + ReadBits(LengthExtra[symbol]);
                var distanceSymbol = Decode(distances);
                if (distanceSymbol >= DistanceBase.Length)
                {
                    throw new InvalidDataException("The deflate distance code is not valid.");
                }

                var copyDistance = DistanceBase[distanceSymbol] + ReadBits(DistanceExtra[distanceSymbol]);
                if (copyDistance > outputLength)
                {
                    throw new InvalidDataException("A deflate back-reference points before the output.");
                }

                EnsureCapacity(length);
                var from = outputLength - copyDistance;
                for (var index = 0; index < length; index++)
                {
                    output[outputLength++] = output[from + index];
                }
            }
        }

        private int Decode(HuffmanTable table)
        {
            var code = 0;
            var first = 0;
            var index = 0;
            for (var length = 1; length <= MaximumCodeLength; length++)
            {
                code |= ReadBits(1);
                var count = table.Counts[length];
                if (code - first < count)
                {
                    return table.Symbols[index + code - first];
                }

                index += count;
                first = (first + count) << 1;
                code <<= 1;
            }

            throw new InvalidDataException("The deflate code is not valid.");
        }

        private int ReadBits(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            while (bitCount < count)
            {
                if (position >= end)
                {
                    throw new InvalidDataException("The deflate stream is truncated.");
                }

                bitBuffer |= (uint)input[position++] << bitCount;
                bitCount += 8;
            }

            var value = (int)(bitBuffer & ((1u << count) - 1));
            bitBuffer >>= count;
            bitCount -= count;
            return value;
        }

        private void Append(byte value)
        {
            EnsureCapacity(1);
            output[outputLength++] = value;
        }

        private void Append(ReadOnlySpan<byte> bytes)
        {
            EnsureCapacity(bytes.Length);
            bytes.CopyTo(output.AsSpan(outputLength));
            outputLength += bytes.Length;
        }

        private void EnsureCapacity(int extra)
        {
            if (extra > maximumBytes - outputLength)
            {
                throw new InvalidDataException("The inflated data exceeds the allowed size.");
            }

            var required = outputLength + extra;
            if (required <= output.Length)
            {
                return;
            }

            var size = output.Length;
            while (size < required)
            {
                size = (int)Math.Min((long)size * 2, maximumBytes);
            }

            Array.Resize(ref output, size);
        }
    }

    /// <summary>A canonical Huffman table in the counts-plus-symbols form that makes decoding a loop.</summary>
    private sealed class HuffmanTable
    {
        private HuffmanTable(int[] counts, int[] symbols)
        {
            Counts = counts;
            Symbols = symbols;
        }

        public int[] Counts { get; }

        public int[] Symbols { get; }

        /// <summary>
        /// Builds a canonical table. An over-subscribed or empty code set is always rejected; an incomplete
        /// set is accepted, because a single-symbol distance alphabet is legal in a real deflate stream and a
        /// code that falls into the unused space simply decodes as an error later.
        /// </summary>
        public static HuffmanTable Build(ReadOnlySpan<int> lengths)
        {
            var counts = new int[MaximumCodeLength + 1];
            var used = 0;
            foreach (var length in lengths)
            {
                if (length is < 0 or > MaximumCodeLength)
                {
                    throw new InvalidDataException("A deflate code length is out of range.");
                }

                counts[length]++;
                if (length > 0)
                {
                    used++;
                }
            }

            // Codes of length zero are unused; the count at index 0 is not part of the canonical layout.
            counts[0] = 0;

            var left = 1;
            for (var length = 1; length <= MaximumCodeLength; length++)
            {
                left = (left << 1) - counts[length];
                if (left < 0)
                {
                    throw new InvalidDataException("The deflate code set is over-subscribed.");
                }
            }

            if (used == 0)
            {
                throw new InvalidDataException("The deflate code set is empty.");
            }

            var offsets = new int[MaximumCodeLength + 2];
            for (var length = 1; length <= MaximumCodeLength; length++)
            {
                offsets[length + 1] = offsets[length] + counts[length];
            }

            var symbols = new int[used];
            for (var symbol = 0; symbol < lengths.Length; symbol++)
            {
                var length = lengths[symbol];
                if (length > 0)
                {
                    symbols[offsets[length]++] = symbol;
                }
            }

            return new HuffmanTable(counts, symbols);
        }
    }
}
