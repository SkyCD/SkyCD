using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.EBook.Exceptions;
using SkyCD.Plugin.Metadata.EBook.Models;

namespace SkyCD.Plugin.Metadata.EBook.Parsing;

/// <summary>
/// Chooses the format reader from the leading bytes: ZIP for EPUB and iBooks, a Palm database header for
/// Mobipocket/Amazon/PalmDOC books, XML for FictionBook. The catalogued extension only refines the format
/// name; it never decides whether a file is read, so a mislabelled book is still recognized.
/// </summary>
internal static class EBookReader
{
    private const int SignatureBytes = 68;

    public static async Task<EBookDocument> ReadAsync(
        IMediaSource source,
        MediaItem item,
        EBookPluginOptions options,
        bool readCover,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (source.Length <= 0)
        {
            throw new EBookDecodeException("the source is empty");
        }

        var count = (int)Math.Min(SignatureBytes, source.Length);
        var prefix = await source.ReadAsync(0, count, cancellationToken).ConfigureAwait(false);
        var signature = prefix.Span;

        if (EBookSignature.IsZip(signature))
        {
            return await EpubReader
                .ReadAsync(source, item.Extension ?? string.Empty, options, readCover, cancellationToken)
                .ConfigureAwait(false);
        }

        if (IsPalmDatabase(signature))
        {
            return await MobipocketReader.ReadAsync(source, options, readCover, cancellationToken).ConfigureAwait(false);
        }

        if (EBookSignature.IsXml(signature))
        {
            return await FictionBookReader.ReadAsync(source, options, readCover, cancellationToken).ConfigureAwait(false);
        }

        throw new EBookDecodeException("the source is not a supported e-book container");
    }

    private static bool IsPalmDatabase(ReadOnlySpan<byte> signature)
    {
        if (signature.Length < 68)
        {
            return false;
        }

        var type = Encoding.ASCII.GetString(signature[60..64]);
        var creator = Encoding.ASCII.GetString(signature[64..68]);
        return (type, creator) is ("BOOK", "MOBI") or ("TEXt", "REAd");
    }
}
