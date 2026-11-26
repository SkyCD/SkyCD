using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.YamlToml.Exceptions;
using SkyCD.Plugin.Metadata.YamlToml.Export;
using SkyCD.Plugin.Metadata.YamlToml.Models;
using SkyCD.Plugin.Metadata.YamlToml.Parsing;

namespace SkyCD.Plugin.Metadata.YamlToml;

/// <summary>
/// Draws a structure-aware first page at index time. The priority sits above the text plugin's plain page so
/// the list thumbnail uses this image. A missing rasterizer omits the artifact and leaves metadata intact.
/// </summary>
public sealed class YamlTomlPreviewPlugin : IMediaPreviewCapability
{
    private const string ThumbnailContentType = "image/png";

    /// <summary>Above the text plugin's default, so this page wins the list thumbnail.</summary>
    internal const int PreviewPriority = 10;

    private readonly YamlTomlPluginOptions options;
    private readonly IYamlTomlThumbnailRenderer renderer;

    public YamlTomlPreviewPlugin()
        : this(YamlTomlPluginOptions.Default, new SkiaYamlTomlThumbnailRenderer())
    {
    }

    public YamlTomlPreviewPlugin(YamlTomlPluginOptions options)
        : this(options, new SkiaYamlTomlThumbnailRenderer())
    {
    }

    /// <summary>Hosts and tests may supply their own renderer.</summary>
    public YamlTomlPreviewPlugin(YamlTomlPluginOptions options, IYamlTomlThumbnailRenderer renderer)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public MediaDescriptor Descriptor =>
        new(YamlTomlSchema.Extensions, YamlTomlSchema.ContentTypes, YamlTomlSchema.Preview,
            Priority: PreviewPriority, IndexTimePreview: true);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] bytes;
        try
        {
            bytes = await YamlTomlSourceReader
                .ReadAsync(item, source, options.MaxSourceBytes, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (YamlTomlSourceTooLargeException)
        {
            return new MediaPreviewResult([]);
        }

        if (bytes.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(0.4));
        var format = YamlTomlDocumentReader.ResolveFormat(item.Extension, item.ContentType);
        var facts = YamlTomlDocumentReader.Read(bytes, format, options, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!facts.Valid || facts.Lines.Count == 0 || !renderer.IsAvailable)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(0.8));
        var png = await renderer.RenderAsync(facts.Lines, options.MaxThumbnailDimension, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(1));

        if (png is null || png.Length == 0)
        {
            return new MediaPreviewResult([]);
        }

        return new MediaPreviewResult([new MediaPreviewArtifact(ThumbnailContentType, png)]);
    }
}
