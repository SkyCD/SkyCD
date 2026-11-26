using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Email.Exceptions;
using SkyCD.Plugin.Metadata.Email.Models;
using SkyCD.Plugin.Metadata.Email.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Email;

public sealed class EmailMetadataPlugin : IMediaMetadataCapability
{
    internal const int MaxSourceBytes = 16 * 1024 * 1024;

    public MediaDescriptor Descriptor => EmailSchema.Descriptor;

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = item.Extension.TrimStart('.').ToLowerInvariant();
        var format = extension is "eml" or "msg" or "pst" ? extension : item.ContentType?.ToLowerInvariant() switch
        {
            "message/rfc822" => "eml",
            "application/vnd.ms-outlook" => "msg",
            "application/vnd.ms-outlook-pst" => "pst",
            _ => throw new EmailDecodeException(extension)
        };

        var length = source.Length;
        var limit = Math.Min(MaxSourceBytes, source.RemainingReadBytes);
        if (length > limit)
        {
            throw new EmailSourceTooLargeException(length, limit);
        }

        if (length <= 0)
        {
            throw new EmailDecodeException(format);
        }

        var bytes = new byte[(int)length];
        for (var offset = 0; offset < bytes.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(256 * 1024, bytes.Length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new EmailDecodeException(format);
            }

            chunk.CopyTo(bytes.AsMemory(offset, count));
            offset += count;
        }

        progress?.Report(new MediaProgress(0.5));
        using var stream = new MemoryStream(bytes, writable: false);
        var values = format == "pst"
            ? EmailMetadataReader.ReadStore(stream, cancellationToken)
            : EmailMetadataReader.ReadMessage(stream, format, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }
}
