using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using SkyCD.Plugin.Metadata.Video.Models;

namespace SkyCD.Plugin.Metadata.Video.Parsing;

/// <summary>
/// Poster frames decoded in-process by the OpenCV videoio backend that ships with the plugin.
/// </summary>
/// <remarks>
/// The bundled backend carries an FFmpeg build, so H.264, HEVC, VP8, VP9, AV1 and MJPEG decode without any
/// external tool. The pinned managed binding has no <see cref="Stream"/> constructor, so the already-bounded
/// source is copied into a private temporary file that is deleted as soon as the frame is read; the copy is
/// never larger than <see cref="VideoPluginOptions.MaxPosterSourceBytes"/>, which the plugin enforces first.
/// Decoding is synchronous native work on a worker task, bounded by
/// <see cref="VideoPluginOptions.DecoderTimeoutMilliseconds"/>. Any failure — an unreadable source, a codec
/// the backend does not carry, a frame larger than the JPEG cap — produces no frame rather than an exception,
/// leaving the metadata result to stand on its own.
/// </remarks>
public sealed class OpenCvFrameExtractor : IVideoFrameExtractor
{
    private readonly int timeoutMilliseconds;

    public OpenCvFrameExtractor(VideoPluginOptions? options = null)
    {
        timeoutMilliseconds = (options ?? VideoPluginOptions.Default).DecoderTimeoutMilliseconds;
    }

    public bool IsAvailable => OpenCvNativeBootstrap.EnsureLoaded();

    public string Describe() => IsAvailable
        ? "Poster frames are decoded in-process by the bundled OpenCV videoio backend."
        : "The bundled OpenCV decoder could not be loaded on this platform; poster frames require an external decoder.";

    public async Task<byte[]?> ExtractJpegAsync(
        Func<Stream, CancellationToken, Task> writeSourceAsync,
        double timestampSeconds,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeSourceAsync);

        if (!IsAvailable)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var workDirectory = Path.Combine(Path.GetTempPath(), $"skycd-video-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(workDirectory, "source");
        var decodeStarted = false;

        try
        {
            Directory.CreateDirectory(workDirectory);

            await using (var file = new FileStream(
                             sourcePath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.Read,
                             64 * 1024,
                             useAsync: true))
            {
                await writeSourceAsync(file, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutMilliseconds > 0)
            {
                timeout.CancelAfter(timeoutMilliseconds);
            }

            // The decode task owns cleanup from here on: a timed-out native call cannot be aborted, so it
            // must be allowed to finish and remove its own temporary file.
            decodeStarted = true;
            var decode = Task.Run(
                () => DecodeAndCleanup(sourcePath, workDirectory, timestampSeconds, maxDimension),
                CancellationToken.None);

            return await decode.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (!decodeStarted)
            {
                TryDeleteDirectory(workDirectory);
            }
        }
    }

    private static byte[]? DecodeAndCleanup(
        string sourcePath,
        string workDirectory,
        double timestampSeconds,
        int maxDimension)
    {
        try
        {
            return Decode(sourcePath, timestampSeconds, maxDimension);
        }
        catch (Exception)
        {
            // Undecodable sources are metadata-only, never provider failures.
            return null;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private static byte[]? Decode(string sourcePath, double timestampSeconds, int maxDimension)
    {
        using var capture = new VideoCapture(sourcePath, VideoCaptureAPIs.FFMPEG);
        if (!capture.IsOpened())
        {
            return null;
        }

        // The FFmpeg backend honours the container's rotation by default; setting it explicitly keeps the
        // poster upright regardless of the backend defaults of the installed native build.
        capture.Set(VideoCaptureProperties.OrientationAuto, 1);

        var fps = capture.Get(VideoCaptureProperties.Fps);
        var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
        var durationSeconds = fps > 0 && frameCount > 0 ? frameCount / fps : 0;

        var targetSeconds = Math.Max(0, timestampSeconds);
        if (durationSeconds > 0)
        {
            targetSeconds = Math.Min(targetSeconds, Math.Max(0, durationSeconds - (1 / fps)));
        }

        capture.Set(VideoCaptureProperties.PosMsec, targetSeconds * 1000);

        using var frame = new Mat();
        if (!capture.Read(frame) || frame.Empty())
        {
            // Seeking can land past the last decodable frame on short or sparsely indexed sources.
            capture.Set(VideoCaptureProperties.PosFrames, 0);
            if (!capture.Read(frame) || frame.Empty())
            {
                return null;
            }
        }

        using var resized = Resize(frame, maxDimension, out var encodedSource);
        if (!Cv2.ImEncode(".jpg", encodedSource, out var encoded) || encoded is null || encoded.Length == 0)
        {
            return null;
        }

        if (encoded.Length <= FFmpegFrameExtractor.MaxJpegBytes)
        {
            return encoded;
        }

        var quality = new ImageEncodingParam(ImwriteFlags.JpegQuality, 60);
        Cv2.ImEncode(".jpg", encodedSource, out var compact, quality);

        return compact is { Length: > 0 } && compact.Length <= FFmpegFrameExtractor.MaxJpegBytes ? compact : null;
    }

    private static Mat? Resize(Mat frame, int maxDimension, out Mat source)
    {
        source = frame;

        var longestEdge = Math.Max(frame.Width, frame.Height);
        if (maxDimension <= 0 || longestEdge <= maxDimension)
        {
            return null;
        }

        var scale = (double)maxDimension / longestEdge;
        var width = Math.Max(1, (int)Math.Round(frame.Width * scale));
        var height = Math.Max(1, (int)Math.Round(frame.Height * scale));

        var resized = new Mat();
        try
        {
            Cv2.Resize(frame, resized, new Size(width, height), 0, 0, InterpolationFlags.Area);
        }
        catch
        {
            resized.Dispose();
            throw;
        }

        source = resized;
        return resized;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A timed-out native read can still hold the temporary file; the OS reclaims it later.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: cleanup is best effort and never changes the result.
        }
    }
}
