using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.EBook.Exceptions;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Publishes the cover embedded in an e-book as a bounded JPEG. The descriptor is marked as an index-time
/// preview, so the indexing pass stores the cover next to the extracted metadata and the browser can show
/// artwork without a second request.
/// </summary>
/// <remarks>
/// A book without a cover, an unreadable or oversized image and a disabled option all yield an empty artifact
/// list, which is the explicit "no cover" state. Extraction of metadata is never affected by a cover failure.
/// </remarks>
public sealed class EBookCoverPlugin : IMediaPreviewCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 10;
    private const string CoverContentType = "image/jpeg";

    private readonly EBookPluginOptions options;
    private EBookSettings settings = EBookSettings.Default;

    public EBookCoverPlugin()
        : this(EBookPluginOptions.Default)
    {
    }

    public EBookCoverPlugin(EBookPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled && current.ExtractCovers
                ? new MediaDescriptor(current.Extensions, EBookSchema.ContentTypes, EBookSchema.Preview,
                    Priority: ProviderPriority, IndexTimePreview: true)
                : new MediaDescriptor([], [], EBookSchema.Preview, Priority: ProviderPriority,
                    IndexTimePreview: true);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = EBookSettings.From(settings);

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

        byte[]? cover;
        try
        {
            progress?.Report(new MediaProgress(0.3));
            var document = await EBookReader
                .ReadAsync(source, item, options, readCover: true, cancellationToken)
                .ConfigureAwait(false);
            cover = document.Cover;
        }
        catch (EBookDecodeException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(0.7));
        if (cover is null)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        byte[] thumbnail;
        try
        {
            thumbnail = EBookCoverRenderer.Render(cover, current.CoverSize, options, cancellationToken);
        }
        catch (EBookCoverException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaPreviewResult([]);
        }

        progress?.Report(new MediaProgress(1));
        return new MediaPreviewResult([new MediaPreviewArtifact(CoverContentType, thumbnail)]);
    }
}
