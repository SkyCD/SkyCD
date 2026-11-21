using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using SkyCD.Plugin.AudioCd.Models;

namespace SkyCD.Plugin.AudioCd.Parsing;

/// <summary>
/// Reads an audio CD table of contents on Windows through the in-box MCI cdaudio device (winmm), so no COM
/// interop or native package is required. The MCI string interface reports track durations rather than absolute
/// start addresses, so track offsets are reconstructed cumulatively from the standard two-second lead-in and the
/// lead-out gains the one-frame correction the freedb specification documents for MCI.
/// </summary>
/// <remarks>
/// MCI ignores data tracks on CD-Extra discs and is not available off Windows, so this reader is a best-effort
/// production default: it returns <c>null</c> for data discs, empty drives and any failure. Multi-session and
/// non-Windows TOC reads are out of scope and reported as unidentified rather than guessed.
/// </remarks>
public sealed class WindowsDiscTocReader : IDiscTocReader
{
    private const int LeadInFrames = 150;
    private const int FramesPerSecond = 75;
    private const int BufferLength = 256;
    private static int aliases;

    public DiscToc? Read(string rootPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(rootPath))
        {
            return null;
        }

        var drive = ToDriveSpecifier(rootPath);
        if (drive is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var alias = "skycd_cd_" + Interlocked.Increment(ref aliases).ToString(CultureInfo.InvariantCulture);
        var opened = false;
        try
        {
            var result = SendString($"open {drive} type cdaudio alias {alias} shareable");
            if (result != 0)
            {
                return null;
            }

            opened = true;
            SendString($"set {alias} time format tmsf");

            var trackCount = ReadNumber($"status {alias} number of tracks");
            if (trackCount is not int count || count is < 1 or > 99)
            {
                return null;
            }

            var durations = new List<int>(count);
            for (var track = 1; track <= count; track++)
            {
                var frames = ReadTrackFrames($"{alias} track {track} length");
                if (frames <= 0)
                {
                    return null;
                }

                durations.Add(frames);
            }

            var offsets = new List<int>(count);
            var offset = LeadInFrames;
            for (var index = 0; index < count; index++)
            {
                offsets.Add(offset);
                offset += durations[index];
            }

            // The MCI device reports the last track one frame short; add it back before deriving the lead-out.
            var leadOut = offset + 1;
            var toc = new DiscToc(1, offsets, leadOut);
            return toc.IsValid() ? toc : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            if (opened)
            {
                SendString($"close {alias}");
            }
        }
    }

    private static string? ToDriveSpecifier(string rootPath)
    {
        try
        {
            var root = Path.GetPathRoot(rootPath);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
            {
                return null;
            }

            return char.ToUpperInvariant(root[0]) + ":";
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static int? ReadNumber(string command)
    {
        var text = GetString(command);
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int ReadTrackFrames(string commandWithAliasAndTrack)
    {
        var text = GetString($"status {commandWithAliasAndTrack}");
        return ParseTimeToFrames(text);
    }

    private static int ParseTimeToFrames(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length is not (2 or 3))
        {
            return 0;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            return 0;
        }

        var frames = parts.Length == 3 &&
                     int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedFrames)
            ? parsedFrames
            : 0;

        return (minutes * 60 + seconds) * FramesPerSecond + frames;
    }

    private static int SendString(string command)
    {
        return mciSendString(command, null, 0, IntPtr.Zero);
    }

    private static string? GetString(string command)
    {
        var buffer = new char[BufferLength];
        var result = mciSendString(command, buffer, buffer.Length, IntPtr.Zero);
        if (result != 0)
        {
            return null;
        }

        var length = Array.IndexOf(buffer, '\0');
        return length < 0 ? new string(buffer) : new string(buffer, 0, length);
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int mciSendString(string command, [Out] char[]? returnString, int returnLength,
        IntPtr callback);
}
