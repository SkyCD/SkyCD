using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Configuration;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.EComic.Exceptions;
using SkyCD.Plugin.Metadata.EComic.Export;
using SkyCD.Plugin.Metadata.EComic.Models;

namespace SkyCD.Plugin.Metadata.EComic;

/// <summary>
/// Publishes the first page of a comic archive as a bounded JPEG cover. The descriptor is marked as an
/// index-time preview, so the indexing pass stores the cover next to the extracted metadata and the browser
/// can show artwork without a second request.
/// </summary>
/// <remarks>
/// A missing first page, an unreadable page, a page above the size or pixel bound and a disabled option all
/// yield an empty artifact list, which is the explicit "no cover" state. Extraction of metadata is never
/// affected by a cover failure.
/// </remarks>
public sealed class EComicCoverPlugin : IMediaPreviewCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 10;
    private const string CoverContentType = "image/jpeg";

    private readonly EComicPluginOptions options;
    private EComicSettings settings = EComicSettings.Default;

    public EComicCoverPlugin()
        : this(EComicPluginOptions.Default)
    {
    }

    public EComicCoverPlugin(EComicPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled && current.ExtractCovers
                ? new MediaDescriptor(current.Extensions, EComicSchema.ContentTypes, EComicSchema.Preview,
                    Priority: ProviderPriority, IndexTimePreview: true)
                : new MediaDescriptor([], [], EComicSchema.Preview, Priority: ProviderPriority,
                    IndexTimePreview: true);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = EComicSettings.From(settings);

    public async Task<MediaPreviewResult> GeneratePreviewAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = settings;
        if (!current.CollectionEnabled || !current.ExtractCovers)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        EComicArchive archive;
        try
        {
            archive = EComicArchive.Open(source, options, cancellationToken);
        }
        catch (EComicArchiveException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        using (archive)
        {
            progress?.Report(new MediaProgress(0.3));
            var page = await archive.ReadFirstPageAsync(options, cancellationToken).ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.7));
            if (page is null)
            {
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            byte[] cover;
            try
            {
                cover = EComicCoverRenderer.Render(page, current.CoverSize, options, cancellationToken);
            }
            catch (EComicCoverException)
            {
                progress?.Report(new MediaProgress(1));
                return new MediaPreviewResult([]);
            }

            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([new MediaPreviewArtifact(CoverContentType, cover)]);
        }
    }
}
