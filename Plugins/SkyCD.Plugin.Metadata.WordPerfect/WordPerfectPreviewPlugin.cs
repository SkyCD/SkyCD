using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;
using SkyCD.Plugin.Metadata.WordPerfect.Export;
using SkyCD.Plugin.Metadata.WordPerfect.Models;
using SkyCD.Plugin.Metadata.WordPerfect.Parsing;

namespace SkyCD.Plugin.Metadata.WordPerfect;

/// <summary>
/// Renders a page thumbnail from the leading text of a WordPerfect document without altering it.
/// </summary>
/// <remarks>
/// WordPerfect 5.x and 6.x bodies are recovered by the bounded legacy importer, so both generations produce
/// a preview; Macintosh and unknown bodies report an empty preview instead of guessing at their layout.
/// Salvage-quality recovery is also reported as "no preview", because that text is extracted from the
/// document area rather than walked as structured records. Protected and malformed documents surface the
/// same failures as extraction, which keeps "no preview" distinguishable from "cannot read". Rendering
/// itself is delegated to <see cref="IWordPerfectThumbnailRenderer"/>.
/// </remarks>
public sealed class WordPerfectPreviewPlugin : IMediaPreviewCapability
{
    private readonly WordPerfectPluginOptions options;
    private readonly IWordPerfectThumbnailRenderer renderer;

    public WordPerfectPreviewPlugin()
        : this(WordPerfectPluginOptions.Default, new SkiaWordPerfectThumbnailRenderer())
    {
    }

    public WordPerfectPreviewPlugin(WordPerfectPluginOptions options)
        : this(options, new SkiaWordPerfectThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public WordPerfectPreviewPlugin(WordPerfectPluginOptions options, IWordPerfectThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(WordPerfectSchema.Extensions, WordPerfectSchema.ContentTypes, WordPerfectSchema.Preview);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!renderer.IsAvailable)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        try
        {
            var data = await WordPerfectSourceReader
                .ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.4));

            var document = WordPerfectDocument.Parse(data.Data, data.Storage);
            if (!document.SupportsStructuredText)
            {
                // Macintosh and unknown bodies use different grammars, so there is no safe text sample to draw.
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            var text = WordPerfectBodyTextImporter
                .ReadStructuredText(data.Data, options.MaxRenderedCharacters, cancellationToken);
            if (text is null)
            {
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            var png = await renderer
                .RenderAsync(text, options.MaxThumbnailDimension, options.MaxRenderedCharacters, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(1));

            if (png is null || png.Length == 0)
            {
                return new MediaPreviewResult([]);
            }

            return new MediaPreviewResult([new MediaPreviewArtifact("image/png", png)]);
        }
        catch (Exception exception) when (exception is WordPerfectDecodeException
                                              or WordPerfectAuthenticationRequiredException
                                              or WordPerfectSourceTooLargeException
                                              or OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WordPerfectDecodeException("the document structure could not be parsed", exception);
        }
    }
}
