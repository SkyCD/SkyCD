using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.Image.Tests;

/// <summary>
/// Covers the adapter that turns the host's bounded random-access reads into the seekable stream a decoder
/// expects: every byte is charged exactly once, backwards seeks and re-reads are free, and reads are batched.
/// </summary>
public sealed class MediaSourceStreamTests
{
    private static readonly byte[] Payload = Enumerable.Range(0, 4096).Select(index => (byte)(index % 251)).ToArray();

    [Fact]
    public async Task SequentialReads_ReturnEveryByteInOrderAndChargeThemOnce()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        var buffer = new byte[Payload.Length];
        var read = await stream.ReadAsync(buffer, CancellationToken.None);

        Assert.Equal(Payload.Length, read);
        Assert.Equal(Payload, buffer);
        Assert.Equal(Payload.Length, source.ChargedBytes);
        Assert.True(source.ReadCount < 8, $"Expected batched reads but saw {source.ReadCount}.");
    }

    [Fact]
    public void SeekingBackwards_IsFreeBecauseBytesAreRetained()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        stream.Position = 1000;
        var first = new byte[16];
        Assert.Equal(16, stream.Read(first, 0, 16));
        var chargedAfterFirstRead = source.ChargedBytes;

        stream.Position = 1000;
        var second = new byte[16];
        Assert.Equal(16, stream.Read(second, 0, 16));

        Assert.Equal(first, second);
        Assert.Equal(chargedAfterFirstRead, source.ChargedBytes);
    }

    [Fact]
    public void SeekFromAllOrigins_ResolvesToTheSamePosition()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        Assert.Equal(100, stream.Seek(100, SeekOrigin.Begin));
        Assert.Equal(150, stream.Seek(50, SeekOrigin.Current));
        Assert.Equal(Payload.Length - 10, stream.Seek(-10, SeekOrigin.End));
        Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
    }

    [Fact]
    public void ReadingAtTheEnd_ReturnsZeroInsteadOfChargingTheBudget()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        stream.Position = Payload.Length;
        var buffer = new byte[16];

        Assert.Equal(0, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public void ReadingPastTheEnd_StopsAtTheSourceLength()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        stream.Position = Payload.Length - 4;
        var buffer = new byte[64];

        Assert.Equal(4, stream.Read(buffer, 0, buffer.Length));
        Assert.Equal(Payload[(Payload.Length - 4)..], buffer[..4]);
    }

    [Fact]
    public void SingleByteReads_AreBatchedRatherThanOneHostCallEach()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        for (var i = 0; i < 512; i++)
        {
            Assert.Equal(Payload[i], stream.ReadByte());
        }

        Assert.True(source.ReadCount <= 4, $"512 single-byte reads took {source.ReadCount} host reads.");
    }

    [Fact]
    public void ExhaustedHostBudget_SurfacesTheHostLimit()
    {
        var source = new ByteArrayMediaSource(Payload, budget: 128);
        using var stream = new MediaSourceStream(source, CancellationToken.None);
        var buffer = new byte[Payload.Length];

        Assert.Throws<InvalidOperationException>(() => stream.Read(buffer, 0, buffer.Length));
    }

    [Fact]
    public async Task CancelledToken_StopsTheRead()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stream.ReadAsync(new byte[64], cancelled.Token).AsTask());
    }

    [Fact]
    public void WritingAndSetLength_AreRejected()
    {
        var source = new ByteArrayMediaSource(Payload);
        using var stream = new MediaSourceStream(source, CancellationToken.None);

        Assert.False(stream.CanWrite);
        Assert.True(stream.CanRead);
        Assert.True(stream.CanSeek);
        Assert.Equal(Payload.Length, stream.Length);
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => stream.SetLength(1));
    }

    [Fact]
    public void DisposedStream_RejectsFurtherUse()
    {
        var source = new ByteArrayMediaSource(Payload);
        var stream = new MediaSourceStream(source, CancellationToken.None);
        stream.Dispose();

        Assert.False(stream.CanRead);
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
    }
}
