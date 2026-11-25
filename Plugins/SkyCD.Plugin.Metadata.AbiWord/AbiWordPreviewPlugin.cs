using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.AbiWord.Export;
using SkyCD.Plugin.Metadata.AbiWord.Models;
using SkyCD.Plugin.Metadata.AbiWord.Parsing;

namespace SkyCD.Plugin.Metadata.AbiWord;

/// <summary>
/// Renders a page thumbnail from the leading text of an AbiWord document without altering it.
/// </summary>
/// <remarks>
/// The same bounded source reader and XML hardening the metadata pass uses are reused here, so gzip input
/// stays bounded and custom entities are never expanded. A document whose XML cannot be parsed reports an
/// empty preview instead of failing the pass, while an oversized or unreadable source keeps the reader's own
/// classification. Rendering itself is delegated to <see cref="IAbiWordThumbnailRenderer"/>.
/// </remarks>
public sealed class AbiWordPreviewPlugin : IMediaPreviewCapability
{
    private readonly AbiWordPluginOptions options;
    private readonly IAbiWordThumbnailRenderer renderer;

    public AbiWordPreviewPlugin()
        : this(AbiWordPluginOptions.Default, new SkiaAbiWordThumbnailRenderer())
    {
    }

    public AbiWordPreviewPlugin(AbiWordPluginOptions options)
        : this(options, new SkiaAbiWordThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public AbiWordPreviewPlugin(AbiWordPluginOptions options, IAbiWordThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(AbiWordSchema.Extensions, AbiWordSchema.ContentTypes, AbiWordSchema.Preview);

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

        var data = await AbiWordSourceReader
            .ReadAsync(item, source, options, cancellationToken)
            .ConfigureAwait(false);

        var text = AbiWordBodyTextReader
            .Read(data, options.MaxRenderedCharacters, options.MaxRenderedParagraphs,
                options.MaxDecompressedBytes, cancellationToken)
            .Trim();
        if (text.Length == 0)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(0.5));
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
}
