using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Pdf.Exceptions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;
using SkyCD.Plugin.Metadata.Pdf.Models;
using SkyCD.Plugin.Metadata.Pdf.Parsing;

namespace SkyCD.Plugin.Metadata.Pdf;

/// <summary>
/// Extracts document metadata and a bounded text excerpt from a PDF without altering it.
/// </summary>
/// <remarks>
/// No script is executed and no external reference is followed: the plugin holds bytes rather than a path, so
/// a document that points at another file or a URL has no way to make this plugin fetch it, and the parser
/// reads structure and text operators only. Fields the document does not carry are omitted rather than
/// published as empty strings, so "unknown" stays distinguishable from "none".
/// </remarks>
public sealed class PdfMetadataPlugin : IMediaMetadataCapability
{
    private readonly PdfPluginOptions options;

    public PdfMetadataPlugin()
        : this(PdfPluginOptions.Default)
    {
    }

    public PdfMetadataPlugin(PdfPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(PdfSchema.Extensions, PdfSchema.ContentTypes, PdfSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await PdfSourceReader.ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        PdfDocument document;
        try
        {
            // Lenient parsing plus skipped missing fonts: a damaged or font-incomplete document should still
            // yield the metadata that is readable, rather than failing the whole extraction.
            document = PdfDocument.Open(data, new ParsingOptions
            {
                UseLenientParsing = true,
                SkipMissingFonts = true
            });
        }
        catch (PdfDocumentEncryptedException exception)
        {
            // Reported distinctly from corruption: the file is intact, it just needs a credential that this
            // plugin will neither request, guess nor store.
            throw new PdfAuthenticationRequiredException("the document is password protected", exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Different parsers surface a credential problem differently, so the encryption dictionary is the
            // tie-breaker: a document that declares one and will not open is auth-required, not corrupt.
            if (DeclaresEncryption(data))
            {
                throw new PdfAuthenticationRequiredException("the document is password protected", exception);
            }

            throw new PdfDecodeException("the document structure could not be parsed", exception);
        }

        using (document)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MediaProgress(0.4));

            var values = new List<MediaMetadataValue>
            {
                new(PdfSchema.Container, MediaMetadataType.Text, Text: "pdf"),
                new(PdfSchema.PageCount, MediaMetadataType.Integer, Integer: document.NumberOfPages),
                new(PdfSchema.IsEncrypted, MediaMetadataType.Boolean, Boolean: document.IsEncrypted)
            };

            if (document.Version > 0)
            {
                values.Add(new MediaMetadataValue(PdfSchema.PdfVersion, MediaMetadataType.Number, Number: document.Version));
            }

            var information = document.Information;
            AddText(values, PdfSchema.Title, information.Title);
            AddText(values, PdfSchema.Author, information.Author);
            AddText(values, PdfSchema.Subject, information.Subject);
            AddText(values, PdfSchema.Keywords, information.Keywords);
            AddText(values, PdfSchema.Creator, information.Creator);
            AddText(values, PdfSchema.Producer, information.Producer);
            AddTimestamp(values, PdfSchema.CreatedAt, information.GetCreatedDateTimeOffset());
            AddTimestamp(values, PdfSchema.ModifiedAt, information.GetModifiedDateTimeOffset());

            AddFirstPageGeometry(values, document);
            progress?.Report(new MediaProgress(0.7));

            AddTextExcerpt(values, document, cancellationToken);

            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values);
        }
    }

    private void AddFirstPageGeometry(List<MediaMetadataValue> values, PdfDocument document)
    {
        if (document.NumberOfPages < 1)
        {
            return;
        }

        try
        {
            var page = document.GetPage(1);
            if (page.Width > 0)
            {
                values.Add(new MediaMetadataValue(PdfSchema.PageWidth, MediaMetadataType.Number, Number: Math.Round(page.Width, 2)));
            }

            if (page.Height > 0)
            {
                values.Add(new MediaMetadataValue(PdfSchema.PageHeight, MediaMetadataType.Number, Number: Math.Round(page.Height, 2)));
            }
        }
        catch (Exception exception) when (exception is PdfDocumentEncryptedException or InvalidOperationException)
        {
            // A first page that cannot be laid out leaves the geometry absent; the rest of the metadata stands.
        }
    }

    /// <summary>
    /// Collects a bounded plain-text excerpt from the leading pages. The bound is a character count rather
    /// than a page count, so a single dense page cannot blow past it, and truncation is reported explicitly
    /// so a consumer knows a search miss may be outside the excerpt.
    /// </summary>
    private void AddTextExcerpt(List<MediaMetadataValue> values, PdfDocument document, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var truncated = false;
        var pageLimit = Math.Min(document.NumberOfPages, options.MaxTextExcerptPages);

        for (var number = 1; number <= pageLimit; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string text;
            try
            {
                text = document.GetPage(number).Text;
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                // A page that cannot be read is skipped; the excerpt keeps what earlier pages produced.
                continue;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var remaining = options.MaxTextExcerptCharacters - builder.Length;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            if (text.Length > remaining)
            {
                builder.Append(text, 0, remaining);
                truncated = true;
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text);
        }

        if (builder.Length == 0)
        {
            // A scanned or image-only document has no text layer; absence is the honest empty state.
            return;
        }

        values.Add(new MediaMetadataValue(PdfSchema.TextExcerpt, MediaMetadataType.Text, Text: builder.ToString()));
        values.Add(new MediaMetadataValue(
            PdfSchema.TextExcerptTruncated, MediaMetadataType.Boolean, Boolean: truncated));
    }

    /// <summary>
    /// Scans the tail of the document for an <c>/Encrypt</c> trailer entry. Only the last kilobyte is
    /// inspected, because that is where the trailer lives, and a document that declares encryption and will
    /// not open needs a credential rather than being reported as corrupt.
    /// </summary>
    private static bool DeclaresEncryption(byte[] data)
    {
        var start = Math.Max(0, data.Length - 1024);
        var tail = Encoding.ASCII.GetString(data, start, data.Length - start);
        return tail.Contains("/Encrypt", StringComparison.Ordinal);
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text.Trim()));
        }
    }

    private static void AddTimestamp(List<MediaMetadataValue> values, string key, DateTimeOffset? timestamp)
    {
        if (timestamp is { } value)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: value));
        }
    }
}
