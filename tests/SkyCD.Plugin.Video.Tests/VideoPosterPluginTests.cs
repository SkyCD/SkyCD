using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Video.Exceptions;
using Xunit;

namespace SkyCD.Plugin.Video.Tests;

public class VideoPosterPluginTests
{
    [Fact]
    public void Descriptor_SharesTheVideoTypesWithAPreviewSchema()
    {
        var descriptor = new VideoPosterPlugin(VideoPluginOptions.Default, new StubExtractor(available: true)).Descriptor;

        Assert.Equal(VideoSchema.Extensions, descriptor.Extensions);
        Assert.Equal("skycd.video.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task WithoutADecoder_ReturnsNoArtifactAndNeverReadsTheSource()
    {
        var video = VideoFixtures.Mp4();
        var source = new ByteArrayMediaSource(video);
        var plugin = new VideoPosterPlugin(VideoPluginOptions.Default, new StubExtractor(available: false));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task WithADecoder_ReturnsTheFrameItProduced()
    {
        var video = VideoFixtures.Mp4();
        var extractor = new StubExtractor(available: true, frame: VideoFixtures.JpegFrame);
        var plugin = new VideoPosterPlugin(VideoPluginOptions.Default, extractor);

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            new ByteArrayMediaSource(video),
            null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal(VideoFixtures.JpegFrame, artifact.Data.ToArray());
    }

    [Fact]
    public async Task TheDecoderReceivesTheWholeSourceAndTheConfiguredBounds()
    {
        var video = VideoFixtures.Mp4();
        var options = new VideoPluginOptions(maxPosterDimension: 320, posterTimestampSeconds: 2.5);
        var extractor = new StubExtractor(available: true, frame: VideoFixtures.JpegFrame);

        await new VideoPosterPlugin(options, extractor).GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            new ByteArrayMediaSource(video),
            null,
            CancellationToken.None);

        Assert.Equal(video, extractor.PipedBytes);
        Assert.Equal(320, extractor.MaxDimension);
        Assert.Equal(2.5, extractor.TimestampSeconds);
    }

    [Fact]
    public async Task SourceTooLargeToPipe_ProducesMetadataOnlyWithoutCallingTheDecoder()
    {
        var video = VideoFixtures.Mp4(mdatBytes: 64 * 1024);
        var options = new VideoPluginOptions(maxPosterSourceBytes: video.Length - 1);
        var extractor = new StubExtractor(available: true, frame: VideoFixtures.JpegFrame);

        var result = await new VideoPosterPlugin(options, extractor).GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            new ByteArrayMediaSource(video),
            null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task DecoderProducingNoFrame_IsMetadataOnlyRatherThanAFailure()
    {
        var video = VideoFixtures.Mp4();
        var plugin = new VideoPosterPlugin(VideoPluginOptions.Default, new StubExtractor(available: true, frame: null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            new ByteArrayMediaSource(video),
            null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task UnreadableContainer_FailsBeforeTheDecoderIsInvoked()
    {
        var extractor = new StubExtractor(available: true, frame: VideoFixtures.JpegFrame);
        var plugin = new VideoPosterPlugin(VideoPluginOptions.Default, extractor);
        var video = VideoFixtures.NotVideo();

        await Assert.ThrowsAsync<VideoDecodeException>(() => plugin.GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length),
            new ByteArrayMediaSource(video),
            null,
            CancellationToken.None));

        Assert.Equal(0, extractor.Calls);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var video = VideoFixtures.Mp4();
        var source = new ByteArrayMediaSource(video);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var plugin = new VideoPosterPlugin(VideoPluginOptions.Default, new StubExtractor(available: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public void NoDecoderIsFound_ForAnExplicitPathThatDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"skycd-absent-{Guid.NewGuid():N}", "ffmpeg");

        // An explicit path that does not resolve yields an unavailable extractor; nothing is downloaded.
        Assert.Null(FFmpegFrameExtractor.DiscoverExecutable(missing));

        var extractor = new FFmpegFrameExtractor(new VideoPluginOptions(ffmpegPath: missing));
        Assert.False(extractor.IsAvailable);
        Assert.Contains("No FFmpeg-compatible decoder", extractor.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnavailableFFmpegExtractor_ProducesNoArtifact()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"skycd-absent-{Guid.NewGuid():N}", "ffmpeg");
        var plugin = new VideoPosterPlugin(
            new VideoPluginOptions(ffmpegPath: missing),
            new FFmpegFrameExtractor(new VideoPluginOptions(ffmpegPath: missing)));

        var video = VideoFixtures.Mp4();
        var source = new ByteArrayMediaSource(video);
        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".mp4", "video/mp4", video.Length), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public void DecoderArguments_BoundTheFrameAndSeekAfterTheInput()
    {
        var arguments = FFmpegFrameExtractor.BuildArguments(2.5, 320);

        Assert.Contains("-i", arguments);
        Assert.Contains("pipe:0", arguments);
        Assert.Contains("2.5", arguments);
        Assert.Contains("-frames:v", arguments);
        Assert.Contains("pipe:1", arguments);
        Assert.Contains(arguments, argument => argument.Contains("min(320,iw)", StringComparison.Ordinal));

        // Seeking must follow the input, because a piped source cannot be seeked.
        Assert.True(arguments.ToList().IndexOf("-ss") > arguments.ToList().IndexOf("-i"),
            "Output seeking is required for a non-seekable piped source.");
    }

    [Fact]
    public void Options_RejectValuesThatCannotBeHonoured()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoPluginOptions(headerScanBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoPluginOptions(maxPosterSourceBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoPluginOptions(maxPosterDimension: 0));
    }

    /// <summary>Stands in for an external decoder so the plugin's contract is testable without one installed.</summary>
    private sealed class StubExtractor : IVideoFrameExtractor
    {
        private readonly bool available;
        private readonly byte[]? frame;

        public StubExtractor(bool available, byte[]? frame = null)
        {
            this.available = available;
            this.frame = frame;
        }

        public int Calls { get; private set; }
        public byte[]? PipedBytes { get; private set; }
        public double TimestampSeconds { get; private set; }
        public int MaxDimension { get; private set; }

        public bool IsAvailable => available;

        public string Describe() => available ? "stub decoder" : "no decoder";

        public async Task<byte[]?> ExtractJpegAsync(
            Func<Stream, CancellationToken, Task> writeSourceAsync,
            double timestampSeconds,
            int maxDimension,
            CancellationToken cancellationToken)
        {
            Calls++;
            TimestampSeconds = timestampSeconds;
            MaxDimension = maxDimension;

            using var buffer = new MemoryStream();
            await writeSourceAsync(buffer, cancellationToken);
            PipedBytes = buffer.ToArray();

            return frame;
        }
    }
}
