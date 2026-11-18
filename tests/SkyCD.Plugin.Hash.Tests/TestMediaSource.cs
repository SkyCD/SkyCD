using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Hash.Tests;

/// <summary>Reads a fixed byte array in bounded pieces, optionally truncating the final read.</summary>
internal sealed class TestMediaSource : IMediaSource
{
    private readonly byte[] data;
    private readonly int maxReadBytes;

    public TestMediaSource(byte[] data, int maxReadBytes = int.MaxValue, bool truncate = false)
    {
        this.data = data;
        this.maxReadBytes = maxReadBytes;
        Truncate = truncate;
    }

    public bool Truncate { get; }

    public long Length => data.Length;

    public int LargestRequestedCount { get; private set; }

    public int ReadCount { get; private set; }

    public ValueTask<ReadOnlyMemory<byte>> ReadAsync(long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        LargestRequestedCount = Math.Max(LargestRequestedCount, count);

        var available = (int)Math.Max(0, Math.Min(count, data.Length - offset));
        if (Truncate && available > 0)
        {
            available--;
        }

        return ValueTask.FromResult<ReadOnlyMemory<byte>>(data.AsMemory((int)offset, available));
    }

    public static byte[] ReadFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

internal sealed class RecordingProgress : IProgress<MediaProgress>
{
    public System.Collections.Generic.List<double> Fractions { get; } = [];

    public void Report(MediaProgress value) => Fractions.Add(value.Fraction);
}
