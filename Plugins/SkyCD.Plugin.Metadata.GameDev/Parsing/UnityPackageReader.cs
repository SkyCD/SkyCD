using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Threading;
using SkiaSharp;
using SkyCD.Plugin.Metadata.GameDev.Exceptions;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Walks a Unity <c>.unitypackage</c> (tar.gz) from its streamed entries. Each asset is a folder containing a
/// <c>pathname</c> text file and an optional <c>preview.png</c>; nothing is extracted and no asset is opened.
/// </summary>
internal static class UnityPackageReader
{
    internal static GameDevFacts? Read(IMediaSource source, CancellationToken cancellationToken)
    {
        var facts = new GameDevFacts { Family = "Unity", Format = "Unity package" };
        GameDevLimitsGuard guard = new();

        try
        {
            using var compressed = new GZipStream(new MediaSourceStream(source, cancellationToken),
                CompressionMode.Decompress);
            using var reader = new TarReader(compressed);
            var entries = 0;
            var assets = 0;
            var previews = 0;

            while (reader.GetNextEntry() is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > GameDevLimits.MaxArchiveEntries)
                {
                    facts.ScanIncomplete = true;
                    break;
                }

                var name = entry.Name ?? string.Empty;
                if (name.EndsWith("/pathname", StringComparison.Ordinal))
                {
                    assets++;
                    var path = guard.ReadText(entry, 4096);
                    if (path is not null)
                    {
                        facts.AddNameSample(SanitizePath(path));
                    }
                }
                else if (name.EndsWith("/preview.png", StringComparison.Ordinal))
                {
                    previews++;
                }
            }

            facts.FileCount = entries;
            facts.AssetCount = assets;
            facts.HasThumbnail = previews > 0;
            return facts;
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or GameDevLimitExceededException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Returns the largest preview PNG in the package as a pass-through artifact.</summary>
    internal static byte[]? TryReadLargestPreview(IMediaSource source, CancellationToken cancellationToken)
    {
        byte[]? best = null;
        long bestArea = 0;
        var budget = GameDevLimits.MaxArchiveBytes;
        var guard = new GameDevLimitsGuard();

        try
        {
            using var compressed = new GZipStream(new MediaSourceStream(source, cancellationToken),
                CompressionMode.Decompress);
            using var reader = new TarReader(compressed);
            var entries = 0;

            while (reader.GetNextEntry() is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++entries > GameDevLimits.MaxArchiveEntries)
                {
                    break;
                }

                if (entry.Name?.EndsWith("/preview.png", StringComparison.Ordinal) != true)
                {
                    continue;
                }

                var bytes = guard.ReadBytes(entry, GameDevLimits.MaxArtifactBytes, ref budget);
                if (bytes is null || bytes.Length == 0)
                {
                    continue;
                }

                using var data = SKData.CreateCopy(bytes);
                using var codec = SKCodec.Create(data);
                if (codec is null)
                {
                    continue;
                }

                long area = (long)codec.Info.Width * codec.Info.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = bytes;
                }
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or GameDevLimitExceededException or NotSupportedException)
        {
            return null;
        }

        if (best is null)
        {
            return null;
        }

        using var imageData = SKData.CreateCopy(best);
        using var image = SKImage.FromEncodedData(imageData);
        if (image is null)
        {
            return null;
        }

        if (Math.Max(image.Width, image.Height) <= GameDevLimits.MaxThumbnailDimension)
        {
            return best;
        }

        using var scaled = PixelPreview.Scale(image, GameDevLimits.MaxThumbnailDimension);
        return scaled is null ? best : PixelPreview.EncodeImage(scaled, out _);
    }

    private static string SanitizePath(string path)
    {
        var cleaned = path.Trim().Replace('\\', '/');
        var segments = cleaned.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length switch
        {
            0 => string.Empty,
            1 => segments[0],
            _ => segments[^2] + "/" + segments[^1]
        };
    }

    /// <summary>Reads tar entry payloads under fixed per-entry and aggregate bounds.</summary>
    private sealed class GameDevLimitsGuard
    {
        internal string? ReadText(TarEntry entry, int maxBytes)
        {
            var bytes = Read(entry, maxBytes);
            return bytes is null ? null : GameDevSource.DecodeText(bytes).Trim();
        }

        internal byte[]? ReadBytes(TarEntry entry, int maxBytes, ref int remainingBudget)
        {
            var limit = Math.Min(maxBytes, remainingBudget);
            if (limit <= 0)
            {
                return null;
            }

            var bytes = Read(entry, limit);
            if (bytes is not null)
            {
                remainingBudget -= bytes.Length;
            }

            return bytes;
        }

        private static byte[]? Read(TarEntry entry, int maxBytes)
        {
            if (entry.DataStream is null || entry.Length <= 0)
            {
                return null;
            }

            if (entry.Length > maxBytes)
            {
                return null;
            }

            using var memory = new MemoryStream((int)entry.Length);
            entry.DataStream.CopyTo(memory, maxBytes + 1);
            return memory.Length > maxBytes ? null : memory.ToArray();
        }
    }
}
