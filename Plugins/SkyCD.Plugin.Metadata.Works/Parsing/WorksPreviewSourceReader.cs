using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Works.Models;
using SkyCD.Plugin.Metadata.Works.Parsing;

namespace SkyCD.Plugin.Metadata.Works;

/// <summary>The bounded inputs a preview can publish, already read through the host's source.</summary>
internal sealed record WorksPreviewSource(byte[]? EmbeddedImage, string? EmbeddedContentType, byte[]? TextBody,
    bool IsWideText, string Storage, string FormatVersion);

/// <summary>
/// Reads the embedded preview stream and the leading document body without materializing anything beyond the
/// configured bounds.
/// </summary>
/// <remarks>
/// Recognition is shared with the metadata capability, so both capabilities agree on what is a Works document
/// and what needs a credential. A document body the plugin cannot interpret yields no text at all, which the
/// preview capability turns into the explicit "no preview" state rather than a partial or invented image.
/// </remarks>
internal static class WorksPreviewSourceReader
{
    private const string PreviewStream = "Preview";
    private const string AlternatePreviewStream = "\u0005Preview";
    private const string ThumbnailStream = "Thumbnail";

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    public static async Task<WorksPreviewSource> ReadAsync(
        MediaItem item,
        IMediaSource source,
        WorksPluginOptions options,
        CancellationToken cancellationToken)
    {
        var buffer = await WorksSourceReader.ReadBytesAsync(item, source, options, cancellationToken)
            .ConfigureAwait(false);

        if (!CompoundFile.HasSignature(buffer))
        {
            // A flat Works 2 file has no compound streams. It is still recognized through the same rules, so a
            // non-Works source fails exactly as the metadata capability fails.
            var flat = WorksSourceReader.ParseBuffer(buffer, options);
            return new WorksPreviewSource(null, null, null, false, flat.Storage, flat.FormatVersion);
        }

        var compound = CompoundFile.Open(buffer);
        var formatVersion = WorksSourceReader.DetectCompoundFormat(compound);

        var (image, contentType) = TryReadEmbeddedImage(compound, options);
        if (image is not null)
        {
            // The embedded preview is the author's own picture; the body does not need to be read.
            return new WorksPreviewSource(image, contentType, null, false, "compound", formatVersion);
        }

        // Works 2000 and 7/8 keep UTF-16 text in CONTENTS; Works 3 and 4 keep single-byte text in MN0.
        var isWideText = formatVersion is "2000" or "7/8";
        var textStream = isWideText ? WorksSourceReader.ContentsStream : WorksSourceReader.Mn0Stream;
        var text = compound.TryReadStream(textStream, options.MaxTextBodyBytes, out var body) ? body : null;

        return new WorksPreviewSource(null, null, text, isWideText, "compound", formatVersion);
    }

    private static (byte[]? Image, string? ContentType) TryReadEmbeddedImage(
        CompoundFile compound,
        WorksPluginOptions options)
    {
        foreach (var name in new[] { PreviewStream, AlternatePreviewStream, ThumbnailStream })
        {
            try
            {
                if (!compound.TryReadStream(name, options.MaxEmbeddedImageBytes, out var image))
                {
                    continue;
                }

                if (image.AsSpan().StartsWith(PngSignature))
                {
                    return (image, "image/png");
                }

                if (image.AsSpan().StartsWith(JpegSignature))
                {
                    return (image, "image/jpeg");
                }
            }
            catch (Exceptions.WorksDecodeException)
            {
                // The embedded preview is optional: a damaged chain costs the thumbnail, not the document.
            }
        }

        return (null, null);
    }
}
