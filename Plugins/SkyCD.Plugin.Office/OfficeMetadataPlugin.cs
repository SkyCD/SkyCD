using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Office.Exceptions;
using SkyCD.Plugin.Office.Models;
using SkyCD.Plugin.Office.Parsing;

namespace SkyCD.Plugin.Office;

/// <summary>
/// Extracts document metadata from Microsoft Office documents without altering or decrypting them.
/// </summary>
/// <remarks>
/// OOXML packages (.docx, .xlsx, .pptx and their macro-enabled and template variants) publish the standard
/// <c>docProps</c> parts, while legacy compound documents publish the OLE summary property sets. The plugin
/// holds bytes through the host's bounded source, never a path, so nothing in a document can make it open
/// another file. A field the document does not carry is omitted rather than published as an empty string or
/// zero, which keeps "unknown" distinguishable from "none". An encrypted OOXML package is identified and
/// reported, never decrypted, and no credential is requested, guessed or stored.
/// </remarks>
public sealed class OfficeMetadataPlugin : IMediaMetadataCapability
{
    private readonly OfficePluginOptions options;

    public OfficeMetadataPlugin()
        : this(OfficePluginOptions.Default)
    {
    }

    public OfficeMetadataPlugin(OfficePluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(OfficeSchema.Extensions, OfficeSchema.ContentTypes, OfficeSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await OfficeSourceReader.ReadAllAsync(item, source, options, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.8));

            cancellationToken.ThrowIfCancellationRequested();
            var values = BuildValues(metadata);
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values);
        }
        catch (Exception exception) when (exception is OfficeDecodeException
                                              or OfficeSourceTooLargeException
                                              or OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OfficeDecodeException("the document structure could not be parsed", exception);
        }
    }

    private static List<MediaMetadataValue> BuildValues(OfficeMetadata metadata)
    {
        var values = new List<MediaMetadataValue>
        {
            new(OfficeSchema.Container, MediaMetadataType.Text, Text: metadata.Container)
        };

        AddText(values, OfficeSchema.DocumentType, metadata.DocumentType);
        if (metadata.IsEncrypted)
        {
            values.Add(new MediaMetadataValue(OfficeSchema.IsEncrypted, MediaMetadataType.Boolean, Boolean: true));
        }

        AddText(values, OfficeSchema.Title, metadata.Title);
        AddText(values, OfficeSchema.Subject, metadata.Subject);
        AddText(values, OfficeSchema.Author, metadata.Author);
        AddText(values, OfficeSchema.Keywords, metadata.Keywords);
        AddText(values, OfficeSchema.Comments, metadata.Comments);
        AddText(values, OfficeSchema.Category, metadata.Category);
        AddText(values, OfficeSchema.ContentStatus, metadata.ContentStatus);
        AddText(values, OfficeSchema.Template, metadata.Template);
        AddText(values, OfficeSchema.LastSavedBy, metadata.LastSavedBy);
        AddText(values, OfficeSchema.RevisionNumber, metadata.RevisionNumber);
        AddText(values, OfficeSchema.Language, metadata.Language);
        AddText(values, OfficeSchema.ApplicationName, metadata.ApplicationName);
        AddText(values, OfficeSchema.ApplicationVersion, metadata.ApplicationVersion);
        AddText(values, OfficeSchema.Company, metadata.Company);
        AddText(values, OfficeSchema.Manager, metadata.Manager);

        AddTimestamp(values, OfficeSchema.CreatedAt, metadata.CreatedAt);
        AddTimestamp(values, OfficeSchema.ModifiedAt, metadata.ModifiedAt);
        AddTimestamp(values, OfficeSchema.LastPrintedAt, metadata.LastPrintedAt);

        AddPositiveInteger(values, OfficeSchema.PageCount, metadata.PageCount);
        AddPositiveInteger(values, OfficeSchema.WordCount, metadata.WordCount);
        AddPositiveInteger(values, OfficeSchema.CharacterCount, metadata.CharacterCount);
        AddPositiveInteger(values, OfficeSchema.SlideCount, metadata.SlideCount);

        return values;
    }

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
