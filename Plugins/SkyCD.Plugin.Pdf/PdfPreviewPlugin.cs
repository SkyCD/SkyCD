using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Pdf.Exceptions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Exceptions;

namespace SkyCD.Plugin.Pdf;

/// <summary>
/// Renders a bounded thumbnail of a PDF's first page.
/// </summary>
/// <remarks>
/// Rendering is delegated to <see cref="IPdfPageRenderer"/> so that a missing or unloadable rasterizer
/// degrades to a metadata-only result instead of failing the extraction. A password-protected document is
/// reported as needing a credential; no password is requested, guessed or stored, and none is passed to the
/// rasterizer.
/// </remarks>
public sealed class PdfPreviewPlugin : IMediaPreviewCapability
{
    private readonly PdfPluginOptions options;
    private readonly IPdfPageRenderer renderer;

    public PdfPreviewPlugin()
        : this(PdfPluginOptions.Default, new DocnetPdfPageRenderer())
    {
    }

    public PdfPreviewPlugin(PdfPluginOptions options)
        : this(options, new DocnetPdfPageRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own rasterizer.</summary>
    public PdfPreviewPlugin(PdfPluginOptions options, IPdfPageRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(PdfSchema.Extensions, PdfSchema.ContentTypes, PdfSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Checked before any read: with no rasterizer there is nothing to produce, so the source budget is
        // left untouched and the metadata capability carries the result alone.
        if (!renderer.IsAvailable)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        var data = await PdfSourceReader.ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        // Confirming the document is readable and unencrypted costs little and turns an unusable rasterizer
        // failure into the correct diagnosis: a password-protected file is an auth-required state, not a
        // rendering problem.
        try
        {
            using var document = PdfDocument.Open(data, new ParsingOptions
            {
                UseLenientParsing = true,
                SkipMissingFonts = true
            });

            if (document.IsEncrypted && document.NumberOfPages < 1)
            {
                throw new PdfAuthenticationRequiredException("the document is password protected");
            }
        }
        catch (PdfDocumentEncryptedException exception)
        {
            throw new PdfAuthenticationRequiredException("the document is password protected", exception);
        }
        catch (PdfAuthenticationRequiredException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new PdfDecodeException("the document structure could not be parsed", exception);
        }

        progress?.Report(new MediaProgress(0.4));

        var png = await renderer
            .RenderPageAsync(data, pageIndex: 0, options.MaxThumbnailDimension, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(1));

        if (png is null || png.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact("image/png", png)]);
    }
}
