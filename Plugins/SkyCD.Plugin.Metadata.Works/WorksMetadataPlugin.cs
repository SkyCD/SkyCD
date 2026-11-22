using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Works.Exceptions;
using SkyCD.Plugin.Metadata.Works.Models;
using SkyCD.Plugin.Metadata.Works.Parsing;

namespace SkyCD.Plugin.Metadata.Works;

/// <summary>
/// Extracts document summary metadata from a Microsoft Works word processor file without altering it.
/// </summary>
/// <remarks>
/// Works 3 and later store the standard OLE summary property sets inside a compound file, while Works 2 files
/// are flat and carry no such summary. The plugin holds bytes through the host's bounded source, never a path,
/// so nothing in a document can make it open another file. A field the document does not carry is omitted
/// rather than published as an empty string or zero, which keeps "unknown" distinguishable from "none".
/// Password-protected documents are never decrypted, and no credential is requested, guessed or stored.
/// </remarks>
public sealed class WorksMetadataPlugin : IMediaMetadataCapability
{
    private const uint TitleProperty = 2;
    private const uint SubjectProperty = 3;
    private const uint AuthorProperty = 4;
    private const uint KeywordsProperty = 5;
    private const uint CommentsProperty = 6;
    private const uint TemplateProperty = 7;
    private const uint LastSavedByProperty = 8;
    private const uint RevisionNumberProperty = 9;
    private const uint LastPrintedProperty = 11;
    private const uint CreatedProperty = 12;
    private const uint ModifiedProperty = 13;
    private const uint PageCountProperty = 14;
    private const uint WordCountProperty = 15;
    private const uint CharacterCountProperty = 16;
    private const uint ApplicationNameProperty = 18;

    private const uint CategoryProperty = 2;
    private const uint ManagerProperty = 14;
    private const uint CompanyProperty = 15;
    private const uint ContentStatusProperty = 27;
    private const uint LanguageProperty = 28;

    private readonly WorksPluginOptions options;

    public WorksMetadataPlugin()
        : this(WorksPluginOptions.Default)
    {
    }

    public WorksMetadataPlugin(WorksPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(WorksSchema.Extensions, WorksSchema.ContentTypes, WorksSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var extracted = await WorksSourceReader.ReadAllAsync(item, source, options, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.5));

            cancellationToken.ThrowIfCancellationRequested();
            var document = new WorksDocument(extracted.FormatVersion, extracted.Storage, extracted.Summary,
                extracted.DocumentSummary);
            progress?.Report(new MediaProgress(0.9));

            var values = BuildValues(document);
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values);
        }
        catch (Exception exception) when (exception is WorksDecodeException
                                              or WorksSourceTooLargeException
                                              or OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new WorksDecodeException("the document structure could not be parsed", exception);
        }
    }

    private static List<MediaMetadataValue> BuildValues(WorksDocument document)
    {
        var values = new List<MediaMetadataValue>
        {
            new(WorksSchema.Container, MediaMetadataType.Text, Text: "works"),
            new(WorksSchema.Storage, MediaMetadataType.Text, Text: document.Storage),
            new(WorksSchema.FormatVersion, MediaMetadataType.Text, Text: document.FormatVersion)
        };

        var summary = document.Summary;
        AddText(values, WorksSchema.Title, Text(summary, TitleProperty));
        AddText(values, WorksSchema.Subject, Text(summary, SubjectProperty));
        AddText(values, WorksSchema.Author, Text(summary, AuthorProperty));
        AddText(values, WorksSchema.Keywords, Text(summary, KeywordsProperty));
        AddText(values, WorksSchema.Comments, Text(summary, CommentsProperty));
        AddText(values, WorksSchema.Template, Text(summary, TemplateProperty));
        AddText(values, WorksSchema.LastSavedBy, Text(summary, LastSavedByProperty));
        AddText(values, WorksSchema.RevisionNumber, Text(summary, RevisionNumberProperty));
        AddText(values, WorksSchema.ApplicationName, Text(summary, ApplicationNameProperty));

        var documentSummary = document.DocumentSummary;
        AddText(values, WorksSchema.Category, Text(documentSummary, CategoryProperty));
        AddText(values, WorksSchema.Manager, Text(documentSummary, ManagerProperty));
        AddText(values, WorksSchema.Company, Text(documentSummary, CompanyProperty));
        AddText(values, WorksSchema.ContentStatus, Text(documentSummary, ContentStatusProperty));
        AddText(values, WorksSchema.Language, Text(documentSummary, LanguageProperty));

        AddTimestamp(values, WorksSchema.LastPrintedAt, Timestamp(summary, LastPrintedProperty));
        AddTimestamp(values, WorksSchema.CreatedAt, Timestamp(summary, CreatedProperty));
        AddTimestamp(values, WorksSchema.ModifiedAt, Timestamp(summary, ModifiedProperty));

        AddPositiveInteger(values, WorksSchema.PageCount, Integer(summary, PageCountProperty));
        AddPositiveInteger(values, WorksSchema.WordCount, Integer(summary, WordCountProperty));
        AddPositiveInteger(values, WorksSchema.CharacterCount, Integer(summary, CharacterCountProperty));

        return values;
    }

    private static string? Text(IReadOnlyDictionary<uint, object> values, uint propertyId)
    {
        if (!values.TryGetValue(propertyId, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            long number => number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static long? Integer(IReadOnlyDictionary<uint, object> values, uint propertyId) =>
        values.TryGetValue(propertyId, out var value) && value is long number ? number : null;

    private static DateTimeOffset? Timestamp(IReadOnlyDictionary<uint, object> values, uint propertyId) =>
        values.TryGetValue(propertyId, out var value) && value is DateTimeOffset timestamp ? timestamp : null;

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
        }
    }

    private static void AddTimestamp(List<MediaMetadataValue> values, string key, DateTimeOffset? timestamp)
    {
        if (timestamp is { } value)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: value));
        }
    }

    private static void AddPositiveInteger(List<MediaMetadataValue> values, string key, long? number)
    {
        if (number is > 0)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: number));
        }
    }
}
