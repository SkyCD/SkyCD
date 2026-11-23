using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Video.Parsing;
using Xunit;

namespace SkyCD.Plugin.Metadata.Video.Tests;

public class CompositeVideoFrameExtractorTests
{
    private static readonly byte[] Frame = [0xFF, 0xD8, 0xFF, 0xD9];

    [Fact]
    public async Task PrimaryFrame_IsReturnedWithoutConsultingTheFallback()
    {
        var primary = new StubExtractor(available: true, frame: Frame);
        var fallback = new StubExtractor(available: true, frame: [0x01, 0x02]);

        var frame = await new CompositeVideoFrameExtractor(primary, fallback)
            .ExtractJpegAsync(WriteNothing, 1.0, 512, CancellationToken.None);

        Assert.Equal(Frame, frame);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(0, fallback.Calls);
    }

    [Fact]
    public async Task EmptyPrimaryFrame_FallsBackToTheExternalDecoder()
    {
        var primary = new StubExtractor(available: true, frame: null);
        var fallback = new StubExtractor(available: true, frame: Frame);

        var frame = await new CompositeVideoFrameExtractor(primary, fallback)
            .ExtractJpegAsync(WriteNothing, 1.0, 512, CancellationToken.None);

        Assert.Equal(Frame, frame);
        Assert.Equal(1, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task FailingPrimary_FallsBackRatherThanFailingTheProvider()
    {
        var primary = new StubExtractor(available: true, frame: null, throws: true);
        var fallback = new StubExtractor(available: true, frame: Frame);

        var frame = await new CompositeVideoFrameExtractor(primary, fallback)
            .ExtractJpegAsync(WriteNothing, 1.0, 512, CancellationToken.None);

        Assert.Equal(Frame, frame);
    }

    [Fact]
    public async Task UnavailablePrimary_UsesAnAvailableFallback()
    {
        var fallback = new StubExtractor(available: true, frame: Frame);
        var composite = new CompositeVideoFrameExtractor(new StubExtractor(available: false), fallback);

        Assert.True(composite.IsAvailable);

        var frame = await composite.ExtractJpegAsync(WriteNothing, 1.0, 512, CancellationToken.None);

        Assert.Equal(Frame, frame);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task NothingAvailable_ProducesNoFrame()
    {
        var composite = new CompositeVideoFrameExtractor(
            new StubExtractor(available: false),
            new StubExtractor(available: false));

        Assert.False(composite.IsAvailable);
        Assert.Null(await composite.ExtractJpegAsync(WriteNothing, 1.0, 512, CancellationToken.None));
    }

    private static Task WriteNothing(Stream stream, CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed class StubExtractor : IVideoFrameExtractor
    {
        private readonly bool available;
        private readonly byte[]? frame;
        private readonly bool throws;

        public StubExtractor(bool available, byte[]? frame = null, bool throws = false)
        {
            this.available = available;
            this.frame = frame;
            this.throws = throws;
        }

        public int Calls { get; private set; }

        public bool IsAvailable => available;

        public string Describe() => available ? "stub available" : "stub unavailable";

        public Task<byte[]?> ExtractJpegAsync(
            Func<Stream, CancellationToken, Task> writeSourceAsync,
            double timestampSeconds,
            int maxDimension,
            CancellationToken cancellationToken)
        {
            Calls++;

            if (throws)
            {
                throw new InvalidOperationException("The primary decoder failed.");
            }

            return Task.FromResult(frame);
        }
    }
}
