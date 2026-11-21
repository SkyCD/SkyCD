using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Configuration;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.EBook.Exceptions;
using SkyCD.Plugin.EBook.Models;
using SkyCD.Plugin.EBook.Parsing;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Publishes the bibliographic metadata of epub, ibook, mobi, azw, azw3, prc and fb2 books: title, authors,
/// publisher, language, subject, description, identifier, publication date and, when the user asked for it,
/// the author names as tag candidates and a preferred display name.
/// </summary>
/// <remarks>
/// Only values the book actually carries are published, so an absent field stays distinguishable from an
/// empty one. Disabling collection, an unreadable or unsupported book, a missing metadata document and an
/// oversized source all yield the same "no values" result instead of a failure, so a scan never stops on one
/// book. The catalogued file name is never rewritten here; the preferred name travels as metadata and the
/// host decides how to present it.
/// </remarks>
public sealed class EBookMetadataPlugin : IMediaMetadataCapability, IPluginSettingsConsumer
{
    private const int ProviderPriority = 10;

    private readonly EBookPluginOptions options;
    private EBookSettings settings = EBookSettings.Default;

    public EBookMetadataPlugin()
        : this(EBookPluginOptions.Default)
    {
    }

    public EBookMetadataPlugin(EBookPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor
    {
        get
        {
            var current = settings;
            return current.CollectionEnabled
                ? new MediaDescriptor(current.Extensions, EBookSchema.ContentTypes, EBookSchema.Metadata,
                    Priority: ProviderPriority)
                : new MediaDescriptor([], [], EBookSchema.Metadata, Priority: ProviderPriority);
        }
    }

    public void ApplySettings(IReadOnlyDictionary<string, string?> settings) =>
        this.settings = EBookSettings.From(settings);

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

        EBookDocument document;
        try
        {
            progress?.Report(new MediaProgress(0.2));
            document = await EBookReader
                .ReadAsync(source, item, options, readCover: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (EBookDecodeException)
        {
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult([]);
        }

        var values = BuildValues(document, current);
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    /// <summary>
    /// Projects the book onto the published schema. Role fields — author tags and the preferred name — appear
    /// only while their user option is enabled, because the host applies exactly what was published.
    /// </summary>
    internal static List<MediaMetadataValue> BuildValues(EBookDocument document, EBookSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        var values = new List<MediaMetadataValue>
        {
            new(EBookSchema.Container, MediaMetadataType.Text, Text: document.Container),
            new(EBookSchema.Format, MediaMetadataType.Text, Text: document.Format)
        };

        AddText(values, EBookSchema.Title, document.Title);
        var authors = EBookText.JoinValues(document.Authors);
        if (settings.TagAuthors)
        {
            AddText(values, EBookSchema.Authors, authors);
        }

        AddText(values, EBookSchema.Publisher, document.Publisher);
        AddText(values, EBookSchema.Language, document.Language);
        AddText(values, EBookSchema.Subject, document.Subject);
        AddText(values, EBookSchema.Description, document.Description);
        AddText(values, EBookSchema.Identifier, document.Identifier);

        if (document.PublishedAt is { } publishedAt)
        {
            values.Add(new MediaMetadataValue(EBookSchema.PublishedAt, MediaMetadataType.Timestamp,
                Timestamp: publishedAt));
        }

        if (document.PublishedYear is { } year && year > 0)
        {
            values.Add(new MediaMetadataValue(EBookSchema.PublishedYear, MediaMetadataType.Integer,
                Integer: year));
        }

        if (document.IsProtected)
        {
            values.Add(new MediaMetadataValue(EBookSchema.Protected, MediaMetadataType.Boolean, Boolean: true));
        }

        var preferred = settings.NameSource switch
        {
            EBookNameSource.Author => authors,
            EBookNameSource.Title => document.Title,
            _ => null
        };
        AddText(values, EBookSchema.PreferredName, preferred);

        return values;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
        }
    }
}
