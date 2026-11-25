using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;
using SkyCD.Plugin.Metadata.WordPerfect.Models;
using SkyCD.Plugin.Metadata.WordPerfect.Parsing;

namespace SkyCD.Plugin.Metadata.WordPerfect;

/// <summary>
/// Extracts the extended document summary from a Corel WordPerfect document without altering it.
/// </summary>
/// <remarks>
/// The plugin holds bytes through the host's bounded source, never a path, so nothing in a document can make
/// it open another file. Protected documents are reported as needing a credential and are never decrypted,
/// and fields the document does not carry are omitted rather than published as empty strings, so "unknown"
/// stays distinguishable from "none".
/// </remarks>
public sealed class WordPerfectMetadataPlugin : IMediaMetadataCapability
{
    private readonly WordPerfectPluginOptions options;

    public WordPerfectMetadataPlugin()
        : this(WordPerfectPluginOptions.Default)
    {
    }

    public WordPerfectMetadataPlugin(WordPerfectPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(WordPerfectSchema.Extensions, WordPerfectSchema.ContentTypes, WordPerfectSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var data = await WordPerfectSourceReader
                .ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new MediaProgress(0.5));

            var document = WordPerfectDocument.Parse(data.Data, data.Storage);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MediaProgress(0.9));

            var values = BuildValues(document);
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values);
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

    private static List<MediaMetadataValue> BuildValues(WordPerfectDocument document)
    {
        var values = new List<MediaMetadataValue>
        {
            new(WordPerfectSchema.Container, MediaMetadataType.Text, Text: "wpd"),
            new(WordPerfectSchema.Storage, MediaMetadataType.Text, Text: document.Storage),
            new(WordPerfectSchema.FormatVersion, MediaMetadataType.Text, Text: document.FormatVersion),
            new(WordPerfectSchema.IsEncrypted, MediaMetadataType.Boolean, Boolean: false)
        };

        foreach (var (tag, key) in WordPerfectSchema.TextFields)
        {
            if (document.Summary.TryGetValue(tag, out var value) && value.Text is { } text)
            {
                values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text));
            }
        }

        foreach (var (tag, key) in WordPerfectSchema.DateFields)
        {
            if (document.Summary.TryGetValue(tag, out var value) && value.Timestamp is { } timestamp)
            {
                values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: timestamp));
            }
        }

        return values;
    }
}
