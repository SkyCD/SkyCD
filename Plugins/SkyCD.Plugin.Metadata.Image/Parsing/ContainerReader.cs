using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.MediaMetadata.Models;
using SkyCD.Plugin.Metadata.Image.Exceptions;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.Plugin.Preview.Export;
using SkyCD.SDK.Capabilities.Media;
namespace SkyCD.Plugin.Metadata.Image.Parsing;

internal static class ContainerReader
{
    internal const int MiB = 1024 * 1024;
    internal const int HeaderBytes = 4 * MiB;
    internal const int CandidateBytes = 8 * MiB;
    internal const int ScanBytes = 32 * MiB;
    internal const int ExtendedBytes = 64 * MiB;
    internal const int DecodeBytes = 128 * MiB;
    internal const int ThumbnailEdge = 512;

    public static async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, Func<Input, Task<ContainerValues?>> read,
        IProgress<MediaProgress>? progress, CancellationToken token)
    {
        ContainerValues? values = null;
        try
        {
            token.ThrowIfCancellationRequested();
            values = await read(new Input(item, source, token)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
        }

        token.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values?.Values ?? []);
    }

    public static async Task<MediaPreviewResult> PreviewAsync(
        MediaItem item, IMediaSource source, Func<Input, Task<MediaPreviewArtifact?>> read,
        IProgress<MediaProgress>? progress, CancellationToken token)
    {
        MediaPreviewArtifact? artifact = null;
        try
        {
            token.ThrowIfCancellationRequested();
            artifact = await read(new Input(item, source, token)).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
        }

        token.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(1));
        return new MediaPreviewResult(artifact is null ? [] : [artifact]);
    }

    private static bool IsUnreadable(Exception exception) => exception is
        IOException or InvalidDataException or ArgumentException or FormatException or OverflowException
        or InvalidOperationException or System.Xml.XmlException or ImageDecodeException
        or Microsoft.Data.Sqlite.SqliteException or ImageMagick.MagickException
        or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException
        or BadImageFormatException or NotSupportedException
        or Docnet.Core.Exceptions.DocnetException or UglyToad.PdfPig.Core.PdfDocumentFormatException
        or UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException;

    internal sealed class Input(MediaItem item, IMediaSource source, CancellationToken token)
    {
        private byte[] prefix = [];

        public string Extension => item.Extension.TrimStart('.').ToLowerInvariant();
        public long Length => source.Length > 0 ? source.Length : item.Length;
        public CancellationToken Token => token;
        public Stream OpenArchive() => new Paint.ArchiveSourceStream(source, Length, token);

        public async Task<byte[]> Prefix(int maxBytes)
        {
            var target = (int)Math.Min(Math.Min(Length, maxBytes), Math.Min(maxBytes, source.RemainingReadBytes) + prefix.Length);
            if (target <= prefix.Length)
            {
                return target <= 0 ? [] : prefix[..target];
            }

            var offset = prefix.Length;
            Array.Resize(ref prefix, target);
            while (offset < target)
            {
                token.ThrowIfCancellationRequested();
                var count = Math.Min(SourcePrefix.ChunkBytes, target - offset);
                var block = await source.ReadAsync(offset, count, token).ConfigureAwait(false);
                block.Span.CopyTo(prefix.AsSpan(offset));
                offset += block.Length;
                if (block.Length != count)
                {
                    Array.Resize(ref prefix, offset);
                    break;
                }
            }
            return prefix;
        }

        public async Task<byte[]?> Whole(int maxBytes)
        {
            if (Length <= 0 || Length > maxBytes || Length - prefix.Length > source.RemainingReadBytes)
            {
                return null;
            }

            var data = await Prefix(maxBytes).ConfigureAwait(false);
            return data.LongLength == Length ? data : null;
        }
    }

    public static MediaPreviewArtifact? Thumbnail(byte[]? data, CancellationToken token, int orientation = 1)
    {
        if (data is null)
        {
            return null;
        }

        var probe = ImageDecoder.Probe(data);
        if (probe.StoredWidth > 8192 || probe.StoredHeight > 8192)
        {
            return null;
        }

        if (probe.Orientation == 1 && orientation is >= 2 and <= 8)
        {
            var rotation = ImageDecoder.RotationDegrees((SKEncodedOrigin)orientation);
            probe = probe with
            {
                Orientation = orientation,
                RotationDegrees = rotation,
                Width = rotation is 90 or 270 ? probe.StoredHeight : probe.StoredWidth,
                Height = rotation is 90 or 270 ? probe.StoredWidth : probe.StoredHeight
            };
        }

        var thumbnail = ImageDecoder.RenderThumbnail(data, probe, ThumbnailEdge, 85, token);
        return thumbnail.Data.Length <= 4 * MiB
            ? new MediaPreviewArtifact(thumbnail.ContentType, thumbnail.Data)
            : null;
    }

    public static MediaPreviewArtifact? Encode(SKBitmap bitmap)
    {
        var hasAlpha = false;
        foreach (var pixel in bitmap.Pixels)
        {
            if (pixel.Alpha != 255)
            {
                hasAlpha = true;
                break;
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        var data = hasAlpha ? PreviewArtifactEncoder.EncodePng(image) : PreviewArtifactEncoder.EncodeJpeg(image, 85);
        return data is { Length: <= 4 * MiB }
            ? new MediaPreviewArtifact(hasAlpha ? "image/png" : "image/jpeg", data)
            : null;
    }
}
