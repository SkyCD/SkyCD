using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Video.Models;

namespace SkyCD.Plugin.Video.Parsing;

/// <summary>
/// Poster frames produced by running an FFmpeg-compatible decoder that the operator supplied.
/// </summary>
/// <remarks>
/// The binary is only ever discovered, never downloaded: an explicit path from
/// <see cref="VideoPluginOptions.FFmpegPath"/>, then <c>SKYCD_FFMPEG_PATH</c>, then a plain <c>PATH</c>
/// lookup. That matters because FFmpeg builds are not license-equivalent — a GPL build with
/// <c>--enable-libx264</c> and an LGPL build impose different obligations — so which one runs is the
/// operator's decision, not the plugin's. When nothing is found, <see cref="IsAvailable"/> is false and the
/// plugin stores metadata only.
/// </remarks>
public sealed class FFmpegFrameExtractor : IVideoFrameExtractor
{
    /// <summary>Upper bound on a returned frame; poster output is small and anything larger is a runaway.</summary>
    internal const int MaxJpegBytes = 4 * 1024 * 1024;

    private readonly int timeoutMilliseconds;

    public FFmpegFrameExtractor(VideoPluginOptions? options = null)
    {
        var effective = options ?? VideoPluginOptions.Default;
        timeoutMilliseconds = effective.DecoderTimeoutMilliseconds;
        ExecutablePath = DiscoverExecutable(effective.FFmpegPath);
    }

    /// <summary>The decoder binary that will be run, or null when none was configured or found.</summary>
    public string? ExecutablePath { get; }

    public bool IsAvailable => ExecutablePath is not null;

    public string Describe() => ExecutablePath is null
        ? "No FFmpeg-compatible decoder was configured or found; poster frames are unavailable and only metadata is stored."
        : $"Poster frames are produced by '{ExecutablePath}', which was configured or found on this machine.";

    public async Task<byte[]?> ExtractJpegAsync(
        Func<Stream, CancellationToken, Task> writeSourceAsync,
        double timestampSeconds,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writeSourceAsync);

        if (ExecutablePath is null)
        {
            return null;
        }

        using var process = new Process { StartInfo = BuildStartInfo(timestampSeconds, maxDimension) };
        if (!process.Start())
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeoutMilliseconds > 0)
        {
            timeout.CancelAfter(timeoutMilliseconds);
        }

        // The writer runs concurrently and must tolerate the decoder exiting before the source is consumed.
        var writer = Task.Run(async () =>
        {
            try
            {
                await writeSourceAsync(process.StandardInput.BaseStream, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                // A closed pipe means the decoder already had what it needed.
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }, CancellationToken.None);

        var frame = await ReadCappedAsync(process.StandardOutput.BaseStream, MaxJpegBytes, timeout.Token)
            .ConfigureAwait(false);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        await writer.ConfigureAwait(false);

        // A zero-length frame means the decoder ran but produced nothing usable for this source.
        return frame.Length == 0 ? null : frame;
    }

    private ProcessStartInfo BuildStartInfo(double timestampSeconds, int maxDimension)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ExecutablePath!,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in BuildArguments(timestampSeconds, maxDimension))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    /// <summary>
    /// Builds the decoder command. Seeking happens after the input so it works with a piped, non-seekable
    /// source, and rotation is left to the decoder's own autorotation rather than being applied twice.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(double timestampSeconds, int maxDimension) =>
    [
        "-hide_banner",
        "-loglevel", "error",
        "-i", "pipe:0",
        "-ss", Math.Max(0, timestampSeconds).ToString("0.###", CultureInfo.InvariantCulture),
        "-frames:v", "1",
        "-vf", $"scale=w='min({maxDimension},iw)':h='min({maxDimension},ih)':force_original_aspect_ratio=decrease",
        "-f", "image2",
        "-c:v", "mjpeg",
        "pipe:1"
    ];

    /// <summary>
    /// Resolves the decoder binary from an explicit path, the environment variable, then <c>PATH</c>. Nothing
    /// is downloaded and no bundled codec pack is assumed.
    /// </summary>
    internal static string? DiscoverExecutable(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? Path.GetFullPath(explicitPath) : null;
        }

        var fromEnvironment = Environment.GetEnvironmentVariable(VideoPluginOptions.FFmpegPathEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return File.Exists(fromEnvironment) ? Path.GetFullPath(fromEnvironment) : null;
        }

        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        foreach (var directory in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim(), name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static async Task<byte[]> ReadCappedAsync(Stream stream, int cap, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];

        while (output.Length < cap)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, cap - output.Length)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Not permitted on this platform; the host timeout still bounds the wait.
        }
    }
}
