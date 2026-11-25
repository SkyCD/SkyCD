using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Video.Models;
using SkyCD.Plugin.Metadata.Video.Parsing;
using Xunit;

namespace SkyCD.Plugin.Metadata.Video.Tests;

/// <summary>
/// Coverage for the bundled in-process decoder. The native runtime is Windows x64 only, so these tests return
/// without asserting on platforms that cannot load it; the metadata and fallback paths stay covered everywhere.
/// </summary>
public class OpenCvFrameExtractorTests
{
    [Fact]
    public void NativeRuntime_LivesNextToTheManagedAssembly_AndLoads()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.GetDirectoryName(typeof(VideoCapture).Assembly.Location)!;

        Assert.True(File.Exists(Path.Combine(directory, "OpenCvSharpExtern.dll")),
            "OpenCvSharpExtern.dll must sit next to OpenCvSharp.dll in the plugin output.");
        Assert.NotEmpty(Directory.EnumerateFiles(directory, "opencv_videoio_ffmpeg*.dll"));

        Assert.True(OpenCvNativeBootstrap.EnsureLoaded());
        Assert.False(string.IsNullOrWhiteSpace(Cv2.GetVersionString()));
    }

    [Fact]
    public async Task ExtractsABoundedPosterFromMjpegAvi_WithoutAnExternalDecoder()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var clip = VideoFixtures.MjpegAvi();
        var extractor = new OpenCvFrameExtractor(new VideoPluginOptions(maxPosterDimension: 32));

        var frame = await extractor.ExtractJpegAsync(
            (stream, token) => WriteAsync(clip, stream, token),
            0.2,
            32,
            CancellationToken.None);

        Assert.NotNull(frame);
        using var decoded = Cv2.ImDecode(frame!, ImreadModes.Color);
        Assert.False(decoded.Empty());
        Assert.True(Math.Max(decoded.Width, decoded.Height) <= 32,
            $"Expected the longest edge to be bounded to 32, got {decoded.Width}x{decoded.Height}.");
    }

    [Fact]
    public async Task TimestampBeyondTheEnd_StillProducesTheLastDecodableFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var clip = VideoFixtures.MjpegAvi(frames: 5, fps: 10);
        var extractor = new OpenCvFrameExtractor(VideoPluginOptions.Default);

        var frame = await extractor.ExtractJpegAsync(
            (stream, token) => WriteAsync(clip, stream, token),
            5.0,
            512,
            CancellationToken.None);

        Assert.NotNull(frame);
        Assert.NotEmpty(frame!);
    }

    [Fact]
    public async Task UndecodableSource_ProducesNoFrameInsteadOfAnException()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var garbage = VideoFixtures.NotVideo(4096);
        var extractor = new OpenCvFrameExtractor(VideoPluginOptions.Default);

        var frame = await extractor.ExtractJpegAsync(
            (stream, token) => WriteAsync(garbage, stream, token),
            1.0,
            512,
            CancellationToken.None);

        Assert.Null(frame);
    }

    [Fact]
    public async Task Plugin_StoresAPoster_WithTheBundledDecoderAlone()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var clip = VideoFixtures.MjpegAvi();
        var options = new VideoPluginOptions(maxPosterDimension: 32);
        var plugin = new VideoPosterPlugin(options, new OpenCvFrameExtractor(options));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".avi", "video/x-msvideo", clip.Length),
            new ByteArrayMediaSource(clip),
            null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.NotEmpty(artifact.Data.ToArray());
    }

    private static async Task WriteAsync(byte[] source, Stream target, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(source);
        await stream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }
}
