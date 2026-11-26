using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Video.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Video.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Video.Tests;

public class VideoMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheDocumentedVideoTypesAndAVersionedSchema()
    {
        var descriptor = new VideoMetadataPlugin().Descriptor;

        Assert.Equal(VideoSchema.Extensions, descriptor.Extensions);
        Assert.Equal(VideoSchema.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.video.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Contains(descriptor.Extensions, extension => extension == ".mp4");
        Assert.Contains(descriptor.Extensions, extension => extension == ".mkv");
        Assert.Contains(descriptor.Extensions, extension => extension == ".webm");
        Assert.Contains(descriptor.Extensions, extension => extension == ".avi");

        // .m4a belongs to the audio plugin, so it must not be claimed here.
        Assert.DoesNotContain(".m4a", descriptor.Extensions);
    }

    [Fact]
    public async Task Mp4_ReadsDurationSizeRotationCodecsAndFrameRate()
    {
        var result = await ExtractAsync(VideoFixtures.Mp4(), ".mp4", "video/mp4");

        Assert.Equal("mp4", Text(result, VideoSchema.Container));
        Assert.Equal(5.0, Number(result, VideoSchema.Duration));
        Assert.Equal(1920L, Integer(result, VideoSchema.Width));
        Assert.Equal(1080L, Integer(result, VideoSchema.Height));
        Assert.Equal(0L, Integer(result, VideoSchema.RotationDegrees));
        Assert.Equal("h264", Text(result, VideoSchema.VideoCodec));
        Assert.Equal("aac", Text(result, VideoSchema.AudioCodec));
        Assert.Equal(24.0, Number(result, VideoSchema.FrameRate));
        Assert.Equal(1L, Integer(result, VideoSchema.VideoStreamCount));
        Assert.Equal(1L, Integer(result, VideoSchema.AudioStreamCount));
        Assert.Equal("Test Video", Text(result, VideoSchema.Title));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public async Task Mp4_RotationIsReadFromTheTrackMatrix(int rotationDegrees)
    {
        var result = await ExtractAsync(VideoFixtures.Mp4(rotationDegrees: rotationDegrees), ".mp4", "video/mp4");

        Assert.Equal(rotationDegrees, Integer(result, VideoSchema.RotationDegrees));
    }

    [Fact]
    public async Task Mp4WithTrailingMovieBox_IsStillReadBySkippingTheMediaPayload()
    {
        var source = new ByteArrayMediaSource(VideoFixtures.Mp4(videoFirst: false, mdatBytes: 2 * 1024 * 1024));

        var result = await new VideoMetadataPlugin().ExtractAsync(
            new MediaItem(".mp4", "video/mp4", source.Length), source, null, CancellationToken.None);

        Assert.Equal(5.0, Number(result, VideoSchema.Duration));
        Assert.Equal(1920L, Integer(result, VideoSchema.Width));

        // Walking the atom tree must not read the media payload it skips over.
        Assert.True(source.ChargedBytes < 64 * 1024,
            $"Reading metadata charged {source.ChargedBytes} bytes of a {source.Length} byte file.");
    }

    [Fact]
    public async Task QuickTime_IsReportedAsItsOwnContainer()
    {
        var result = await ExtractAsync(VideoFixtures.QuickTime(), ".mov", "video/quicktime");

        Assert.Equal("mov", Text(result, VideoSchema.Container));
        Assert.Equal("hevc", Text(result, VideoSchema.VideoCodec));
        Assert.Null(Text(result, VideoSchema.Title));
    }

    [Fact]
    public async Task VideoWithoutAnAudioTrack_OmitsAudioFieldsRatherThanZeroingThem()
    {
        var result = await ExtractAsync(VideoFixtures.Mp4(withAudioTrack: false), ".mp4", "video/mp4");

        Assert.Equal(1L, Integer(result, VideoSchema.VideoStreamCount));
        Assert.Null(Integer(result, VideoSchema.AudioStreamCount));
        Assert.Null(Text(result, VideoSchema.AudioCodec));
    }

    [Fact]
    public async Task UnknownCodec_IsPublishedAsItsRawIdentifierInsteadOfFailing()
    {
        // Container recognition is separate from codec availability: the tree is read whatever the codec is.
        var result = await ExtractAsync(VideoFixtures.Mp4(videoCodec: "zzzz"), ".mp4", "video/mp4");

        Assert.Equal("zzzz", Text(result, VideoSchema.VideoCodec));
        Assert.Equal(5.0, Number(result, VideoSchema.Duration));
        Assert.Equal(1920L, Integer(result, VideoSchema.Width));
    }

    [Fact]
    public async Task Matroska_ReadsInfoTracksAndStreamFacts()
    {
        var result = await ExtractAsync(VideoFixtures.Matroska(), ".mkv", "video/x-matroska");

        Assert.Equal("mkv", Text(result, VideoSchema.Container));
        Assert.Equal(5.0, Number(result, VideoSchema.Duration));
        Assert.Equal(1280L, Integer(result, VideoSchema.Width));
        Assert.Equal(720L, Integer(result, VideoSchema.Height));
        Assert.Equal("vp9", Text(result, VideoSchema.VideoCodec));
        Assert.Equal("opus", Text(result, VideoSchema.AudioCodec));
        Assert.Equal(30.0, Number(result, VideoSchema.FrameRate));
        Assert.Equal("Test Matroska", Text(result, VideoSchema.Title));

        // Without a projection element there is no declared rotation, so the field is absent, not zero.
        Assert.Null(Integer(result, VideoSchema.RotationDegrees));
    }

    [Fact]
    public async Task Matroska_ReadsRotationFromTheProjectionElement()
    {
        var result = await ExtractAsync(
            VideoFixtures.Matroska(projectionRoll: 270.0), ".mkv", "video/x-matroska");

        Assert.Equal(270L, Integer(result, VideoSchema.RotationDegrees));
    }

    [Fact]
    public async Task Matroska_PrefersDisplayDimensionsOverPixelDimensions()
    {
        var result = await ExtractAsync(
            VideoFixtures.Matroska(pixelWidth: 1920, pixelHeight: 1080, displayWidth: 1440, displayHeight: 1080),
            ".mkv", "video/x-matroska");

        Assert.Equal(1440L, Integer(result, VideoSchema.Width));
        Assert.Equal(1080L, Integer(result, VideoSchema.Height));
    }

    [Fact]
    public async Task MatroskaWithUnknownSegmentSize_IsStillRead()
    {
        var result = await ExtractAsync(
            VideoFixtures.Matroska(unknownSegmentSize: true), ".mkv", "video/x-matroska");

        Assert.Equal(5.0, Number(result, VideoSchema.Duration));
        Assert.Equal("vp9", Text(result, VideoSchema.VideoCodec));
    }

    [Fact]
    public async Task Webm_IsReportedAsItsOwnContainer()
    {
        var result = await ExtractAsync(VideoFixtures.Webm(), ".webm", "video/webm");

        Assert.Equal("webm", Text(result, VideoSchema.Container));
        Assert.Equal("Test WebM", Text(result, VideoSchema.Title));
    }

    [Fact]
    public async Task Avi_ReadsMainHeaderStreamHeaderAndFormats()
    {
        var result = await ExtractAsync(VideoFixtures.Avi(), ".avi", "video/x-msvideo");

        Assert.Equal("avi", Text(result, VideoSchema.Container));
        Assert.Equal(10.0, Number(result, VideoSchema.Duration));
        Assert.Equal(640L, Integer(result, VideoSchema.Width));
        Assert.Equal(480L, Integer(result, VideoSchema.Height));
        Assert.Equal("h264", Text(result, VideoSchema.VideoCodec));
        Assert.Equal("mp3", Text(result, VideoSchema.AudioCodec));
        Assert.Equal(25.0, Number(result, VideoSchema.FrameRate));
        Assert.Equal(1L, Integer(result, VideoSchema.VideoStreamCount));
        Assert.Equal(1L, Integer(result, VideoSchema.AudioStreamCount));

        // AVI has no standard rotation field, so nothing is claimed rather than a default of zero.
        Assert.Null(Integer(result, VideoSchema.RotationDegrees));
    }

    [Fact]
    public async Task AviWithoutAnAudioStream_OmitsAudioFields()
    {
        var result = await ExtractAsync(VideoFixtures.Avi(withAudioStream: false), ".avi", "video/x-msvideo");

        Assert.Null(Text(result, VideoSchema.AudioCodec));
        Assert.Null(Integer(result, VideoSchema.AudioStreamCount));
    }

    [Theory]
    [InlineData("video/mp4", "mp4")]
    [InlineData("video/x-matroska", "mkv")]
    [InlineData("video/webm", "webm")]
    [InlineData("video/x-msvideo", "avi")]
    public async Task ContainerIsDetectedFromBytes_EvenWithAWrongExtension(
        string contentType, string expectedContainer)
    {
        var video = expectedContainer switch
        {
            "mp4" => VideoFixtures.Mp4(),
            "mkv" => VideoFixtures.Matroska(),
            "webm" => VideoFixtures.Webm(),
            _ => VideoFixtures.Avi()
        };

        var result = await ExtractAsync(video, ".unknown", contentType);

        Assert.Equal(expectedContainer, Text(result, VideoSchema.Container));
    }

    [Theory]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".mkv", "video/x-matroska")]
    [InlineData(".webm", "video/webm")]
    [InlineData(".avi", "video/x-msvideo")]
    public async Task NonVideoBytes_FailWithADecodeErrorForEachClaimedExtension(string extension, string contentType)
    {
        await Assert.ThrowsAsync<VideoDecodeException>(
            () => ExtractAsync(VideoFixtures.NotVideo(), extension, contentType));
    }

    [Theory]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".mkv", "video/x-matroska")]
    [InlineData(".avi", "video/x-msvideo")]
    public async Task TruncatedContainers_FailWithADecodeError(string extension, string contentType)
    {
        var video = extension switch
        {
            ".mp4" => VideoFixtures.Mp4(),
            ".mkv" => VideoFixtures.Matroska(),
            _ => VideoFixtures.Avi()
        };

        await Assert.ThrowsAsync<VideoDecodeException>(
            () => ExtractAsync(VideoFixtures.HeaderOnly(video, 24), extension, contentType));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<VideoDecodeException>(() => ExtractAsync([], ".mp4", "video/mp4"));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var video = VideoFixtures.Mp4();
        var source = new ByteArrayMediaSource(video);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new VideoMetadataPlugin().ExtractAsync(
            new MediaItem(".mp4", "video/mp4", video.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task MetadataReading_ChargesOnlyForTheHeadersItWalks()
    {
        var video = VideoFixtures.Mp4(mdatBytes: 4 * 1024 * 1024);
        var source = new ByteArrayMediaSource(video);

        var result = await new VideoMetadataPlugin().ExtractAsync(
            new MediaItem(".mp4", "video/mp4", video.Length), source, null, CancellationToken.None);

        Assert.Equal(5.0, Number(result, VideoSchema.Duration));
        Assert.True(source.ChargedBytes < 64 * 1024,
            $"Metadata charged {source.ChargedBytes} of {video.Length} bytes; payloads must be skipped, not read.");
    }

    [Fact]
    public async Task ExhaustedHostBudget_SurfacesAsAProviderFailure()
    {
        var video = VideoFixtures.Mp4();
        var source = new ByteArrayMediaSource(video, budget: 8);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new VideoMetadataPlugin().ExtractAsync(
            new MediaItem(".mp4", "video/mp4", video.Length), source, null, CancellationToken.None));
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = VideoSchema.Metadata;

        foreach (var video in new[]
                 {
                     VideoFixtures.Mp4(), VideoFixtures.Matroska(), VideoFixtures.Webm(), VideoFixtures.Avi()
                 })
        {
            var result = await ExtractAsync(video, ".mp4", null);

            Assert.NotEmpty(result.Values);
            foreach (var value in result.Values)
            {
                var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
                Assert.NotNull(field);
                Assert.Equal(field!.Type, value.Type);
            }

            Assert.Equal(result.Values.Count,
                result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var video = VideoFixtures.Mp4();
        var before = (byte[])video.Clone();

        await ExtractAsync(video, ".mp4", "video/mp4");

        Assert.Equal(before, video);
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] video, string extension, string? contentType) =>
        new VideoMetadataPlugin().ExtractAsync(
            new MediaItem(extension, contentType, video.Length),
            new ByteArrayMediaSource(video),
            progress: null,
            CancellationToken.None);

    internal static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    internal static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    internal static double? Number(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Number;
}
