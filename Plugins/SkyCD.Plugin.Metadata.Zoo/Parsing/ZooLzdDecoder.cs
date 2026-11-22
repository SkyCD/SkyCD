using System;
using System.IO;
using System.Threading;
using SkyCD.Plugin.Metadata.Zoo.Exceptions;

namespace SkyCD.Plugin.Metadata.Zoo.Parsing;

/// <summary>
/// Decoder for the ZOO LZD packing method (method 1). It is an LZW stream with 9-13 bit codes stored
/// least-significant-bit first, a CLEAR code (256) and an end-of-file code (257).
/// </summary>
internal static class ZooLzdDecoder
{
    private const int ClearCode = 256;
    private const int EndOfFileCode = 257;
    private const int FirstFreeCode = 258;
    private const int MaxBits = 13;
    private const int MaxCodes = 8192;
    private const int OutputBufferBytes = 81_920;

    /// <summary>Decodes a compressed member into <paramref name="output"/> and returns the byte count.</summary>
    public static long Decode(Stream compressed, Stream output, long expectedBytes, ZooCrc16 crc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(crc);
        if (expectedBytes < 0)
        {
            throw new ZooDamagedArchiveException();
        }

        var reader = new BitReader(compressed);
        var prefix = new int[MaxCodes + 2];
        var suffix = new byte[MaxCodes + 2];
        var stack = new byte[MaxCodes + 1];
        var writer = new OutputWriter(output, crc);

        try
        {
            var bitCount = 9;
            var maxCode = 512;
            var freeCode = FirstFreeCode;
            var oldCode = -1;
            var firstChar = 0;
            var hasPrevious = false;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var code = reader.Read(bitCount);
                if (code < 0)
                {
                    throw new ZooDamagedArchiveException();
                }

                if (code == EndOfFileCode)
                {
                    break;
                }

                if (code == ClearCode)
                {
                    bitCount = 9;
                    maxCode = 512;
                    freeCode = FirstFreeCode;
                    oldCode = -1;
                    hasPrevious = false;
                    continue;
                }

                var inputCode = code;
                var stackTop = 0;

                if (code >= freeCode)
                {
                    if (code > freeCode || !hasPrevious)
                    {
                        throw new ZooDamagedArchiveException();
                    }

                    stack[stackTop++] = (byte)firstChar;
                    code = oldCode;
                }

                while (code > 255)
                {
                    if (code < FirstFreeCode || code >= freeCode || stackTop >= stack.Length)
                    {
                        throw new ZooDamagedArchiveException();
                    }

                    stack[stackTop++] = suffix[code];
                    code = prefix[code];
                }

                firstChar = code;
                if (stackTop >= stack.Length)
                {
                    throw new ZooDamagedArchiveException();
                }

                stack[stackTop++] = (byte)code;
                while (stackTop > 0)
                {
                    if (writer.Total >= expectedBytes)
                    {
                        throw new ZooDamagedArchiveException();
                    }

                    writer.Write(stack[--stackTop]);
                }

                if (hasPrevious && freeCode < MaxCodes)
                {
                    prefix[freeCode] = oldCode;
                    suffix[freeCode] = (byte)firstChar;
                    freeCode++;
                }

                if (freeCode >= maxCode && bitCount < MaxBits)
                {
                    bitCount++;
                    maxCode <<= 1;
                }

                oldCode = inputCode;
                hasPrevious = true;
            }

            return writer.Total;
        }
        finally
        {
            writer.Flush();
        }
    }

    private sealed class BitReader(Stream stream)
    {
        private uint buffer;
        private int bits;

        public int Read(int count)
        {
            while (bits < count)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    return -1;
                }

                buffer |= (uint)value << bits;
                bits += 8;
            }

            var result = (int)(buffer & ((1u << count) - 1));
            buffer >>= count;
            bits -= count;
            return result;
        }
    }

    private sealed class OutputWriter(Stream output, ZooCrc16 crc)
    {
        private readonly byte[] buffer = new byte[OutputBufferBytes];
        private int count;

        public long Total { get; private set; }

        public void Write(byte value)
        {
            buffer[count++] = value;
            Total++;
            if (count == buffer.Length)
            {
                Flush();
            }
        }

        public void Flush()
        {
            if (count == 0)
            {
                return;
            }

            crc.Append(buffer.AsSpan(0, count));
            output.Write(buffer, 0, count);
            count = 0;
        }
    }
}
