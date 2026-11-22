using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.EComic.Exceptions;
using SkyCD.Plugin.Metadata.EComic.Models;

namespace SkyCD.Plugin.Metadata.EComic;

/// <summary>
/// Publishes the ComicInfo metadata of cbz/cbr/cb7 archives: series, title, issue facts and, when the user
/// asked for it, the writer names as tag candidates and a preferred display name.
/// </summary>
/// <remarks>
/// Only values the document actually carries are published, so an absent field stays distinguishable from an
/// empty one. Disabling collection, an unreadable archive, a missing or malformed ComicInfo and an oversized
/// document all yield the same "no values" result instead of a failure, so a scan never stops on one comic.
/// The catalogued file name is never rewritten here; the preferred name travels as metadata and the host
/// decides how to present it.
/// </remarks>
public sealed class EComicMetadataPlugin : IMediaMetadataCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 10;

    private readonly EComicPluginOptions options;
    private EComicSettings settings = EComicSettings.Default;

    public EComicMetadataPlugin()
        : this(EComicPluginOptions.Default)
    {
    }

    public EComicMetadataPlugin(EComicPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled
                ? new MediaDescriptor(current.Extensions, EComicSchema.ContentTypes, EComicSchema.Metadata,
                    Priority: ProviderPriority)
                : new MediaDescriptor([], [], EComicSchema.Metadata, Priority: ProviderPriority);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = EComicSettings.From(settings);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var current = settings;
        if (!current.CollectionEnabled)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        EComicArchive archive;
        try
        {
            archive = EComicArchive.Open(source, options, cancellationToken);
        }
        catch (EComicArchiveException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        using (archive)
        {
            progress?.Report(new MediaProgress(0.3));
            var information = await archive.ReadComicInfoAsync(options, cancellationToken).ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.8));

            var values = BuildValues(information, archive.Container, current);
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values);
        }
    }

    /// <summary>
    /// Projects ComicInfo onto the published schema. Role fields — writer tags and the preferred name — appear
    /// only while their user option is enabled, because the host applies exactly what was published.
    /// </summary>
    internal static List<MediaMetadataValue> BuildValues(ComicInfo information, string container, EComicSettings settings)
    {
        ArgumentNullException.ThrowIfNull(information);
        ArgumentNullException.ThrowIfNull(settings);

        var values = new List<MediaMetadataValue>
        {
            new(EComicSchema.Container, MediaMetadataType.Text, Text: container)
        };

        AddText(values, EComicSchema.Series, information.Series);
        AddText(values, EComicSchema.Title, information.Title);
        AddText(values, EComicSchema.Number, information.Number);
        AddText(values, EComicSchema.Volume, information.Volume);
        if (settings.TagWriters)
        {
            AddText(values, EComicSchema.Writer, information.Writer);
        }

        AddText(values, EComicSchema.Publisher, information.Publisher);
        AddText(values, EComicSchema.Genre, information.Genre);
        AddInteger(values, EComicSchema.Year, information.Year);
        AddInteger(values, EComicSchema.PageCount, information.PageCount);
        AddText(values, EComicSchema.LanguageIso, information.LanguageIso);
        AddText(values, EComicSchema.Summary, information.Summary);

        var preferred = settings.NameSource switch
        {
            EComicNameSource.Series => information.Series,
            EComicNameSource.Title => information.Title,
            _ => null
        };
        AddText(values, EComicSchema.PreferredName, preferred);

        return values;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
        }
    }

    private static void AddInteger(List<MediaMetadataValue> values, string key, int? number)
    {
        if (number is > 0)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: number));
        }
    }
}
