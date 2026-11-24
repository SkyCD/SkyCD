using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1025: the shipped icon must be a multi-size disc with a real alpha channel so the
/// taskbar and title bars never draw the old opaque square.
/// </summary>
public sealed class AppIconAssetTests
{
    private static readonly int[] RequiredSizes = [16, 24, 32, 48, 64, 256];

    [Fact]
    public void Icon_ShipsRequiredFrameSizes()
    {
        var icon = IconFile.Read(FindIconPath());

        Assert.Equal(RequiredSizes, icon.Frames.Select(frame => frame.Size).Order().ToArray());
    }

    [Fact]
    public void Icon_FramesAreTransparentOutsideTheDisc()
    {
        var icon = IconFile.Read(FindIconPath());

        foreach (var frame in icon.Frames)
        {
            var center = (frame.Size - 1) / 2.0;
            var outsideAlpha = 0;
            for (var y = 0; y < frame.Size; y++)
            {
                for (var x = 0; x < frame.Size; x++)
                {
                    var distance = Math.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    if (distance > frame.Size / 2.0)
                    {
                        outsideAlpha = Math.Max(outsideAlpha, frame.Alpha[y * frame.Size + x]);
                    }
                }
            }

            Assert.True(outsideAlpha <= 16, $"{frame.Size}x{frame.Size} frame has alpha {outsideAlpha} outside the disc.");
            Assert.Equal(0, frame.Alpha[0]);
            Assert.Equal(0, frame.Alpha[frame.Size - 1]);
            Assert.Equal(0, frame.Alpha[(frame.Size - 1) * frame.Size]);
            Assert.Equal(0, frame.Alpha[(frame.Size - 1) * frame.Size + frame.Size - 1]);
        }
    }

    [Fact]
    public void Icon_EveryFrameUsesFullAlphaRange()
    {
        var icon = IconFile.Read(FindIconPath());

        foreach (var frame in icon.Frames)
        {
            Assert.False(frame.IsFullyOpaque, $"{frame.Size}x{frame.Size} frame has no transparent pixels.");
            Assert.Contains(frame.Alpha, alpha => alpha == 255);
        }
    }

    private static string FindIconPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return Path.Combine(current.FullName, "src", "SkyCD.App", "Assets", "skycd.ico");
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed class IconFrame
    {
        public IconFrame(int size, byte[] alpha)
        {
            Size = size;
            Alpha = alpha;
        }

        public int Size { get; }

        public byte[] Alpha { get; }

        public bool IsFullyOpaque => Alpha.All(alpha => alpha == 255);
    }

    private sealed class IconFile
    {
        private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

        private IconFile(IReadOnlyList<IconFrame> frames)
        {
            Frames = frames;
        }

        public IReadOnlyList<IconFrame> Frames { get; }

        public static IconFile Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 6, "The icon file is truncated.");
            Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
            Assert.True(count > 0, "The icon file has no frames.");

            var frames = new List<IconFrame>(count);
            for (var index = 0; index < count; index++)
            {
                var entry = 6 + index * 16;
                var widthByte = bytes[entry];
                var size = widthByte == 0 ? 256 : widthByte;
                var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8));
                var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 12));
                var frameBytes = bytes.AsSpan(offset, length);
                var alpha = frameBytes.StartsWith(PngSignature)
                    ? DecodePngAlpha(frameBytes, size)
                    : DecodeDibAlpha(frameBytes, size);
                frames.Add(new IconFrame(size, alpha));
            }

            return new IconFile(frames);
        }

        private static byte[] DecodeDibAlpha(ReadOnlySpan<byte> data, int size)
        {
            var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data);
            var width = BinaryPrimitives.ReadInt32LittleEndian(data[4..]);
            var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[8..])) / 2;
            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
            Assert.Equal(size, width);
            Assert.Equal(size, height);
            Assert.Equal(32, bitCount);

            var stride = width * 4;
            var alpha = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // DIB rows are stored bottom-up.
                    alpha[(height - 1 - y) * width + x] = data[headerSize + y * stride + x * 4 + 3];
                }
            }

            return alpha;
        }

        private static byte[] DecodePngAlpha(ReadOnlySpan<byte> png, int expectedSize)
        {
            using var stream = new MemoryStream(png.ToArray());
            var signature = new byte[PngSignature.Length];
            stream.ReadExactly(signature);
            Assert.Equal(PngSignature, signature);

            var width = 0;
            var height = 0;
            var compressed = new MemoryStream();
            while (true)
            {
                var lengthBuffer = new byte[4];
                if (stream.Read(lengthBuffer, 0, 4) != 4)
                {
                    throw new InvalidDataException("PNG data ended before IEND.");
                }

                var length = BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
                var typeBuffer = new byte[4];
                stream.ReadExactly(typeBuffer);
                var payload = new byte[length];
                stream.ReadExactly(payload);
                stream.Seek(4, SeekOrigin.Current);

                var type = Encoding.ASCII.GetString(typeBuffer);
                if (type == "IHDR")
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(payload);
                    height = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(4));
                    Assert.Equal(8, payload[8]);
                    Assert.Equal(6, payload[9]);
                    Assert.Equal(0, payload[10]);
                    Assert.Equal(0, payload[11]);
                    Assert.Equal(0, payload[12]);
                }
                else if (type == "IDAT")
                {
                    compressed.Write(payload);
                }
                else if (type == "IEND")
                {
                    break;
                }
            }

            Assert.Equal(expectedSize, width);
            Assert.Equal(expectedSize, height);

            compressed.Position = 0;
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            inflater.CopyTo(raw);
            var scanlines = raw.ToArray();

            var stride = width * 4;
            Assert.Equal((stride + 1) * height, scanlines.Length);
            var pixels = Unfilter(scanlines, width, height, stride);

            var alpha = new byte[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    alpha[y * width + x] = pixels[y * stride + x * 4 + 3];
                }
            }

            return alpha;
        }

        private static byte[] Unfilter(byte[] scanlines, int width, int height, int stride)
        {
            var pixels = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                var source = y * (stride + 1);
                var filter = scanlines[source];
                var row = y * stride;
                for (var x = 0; x < stride; x++)
                {
                    int left = x >= 4 ? pixels[row + x - 4] : 0;
                    int up = y > 0 ? pixels[row - stride + x] : 0;
                    int upLeft = x >= 4 && y > 0 ? pixels[row - stride + x - 4] : 0;
                    var value = scanlines[source + 1 + x];
                    pixels[row + x] = filter switch
                    {
                        0 => (byte)value,
                        1 => (byte)(value + left),
                        2 => (byte)(value + up),
                        3 => (byte)(value + (left + up) / 2),
                        4 => (byte)(value + Paeth(left, up, upLeft)),
                        _ => throw new InvalidDataException("Unsupported PNG filter."),
                    };
                }
            }

            return pixels;
        }

        private static int Paeth(int left, int up, int upLeft)
        {
            var estimate = left + up - upLeft;
            var leftDistance = Math.Abs(estimate - left);
            var upDistance = Math.Abs(estimate - up);
            var upLeftDistance = Math.Abs(estimate - upLeft);
            if (leftDistance <= upDistance && leftDistance <= upLeftDistance)
            {
                return left;
            }

            return upDistance <= upLeftDistance ? up : upLeft;
        }
    }
}
