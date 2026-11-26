using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Notebook.Models;
using SkyCD.Plugin.Metadata.Notebook.Parsing;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Notebook;

/// <summary>
/// Publishes the format version, kernel language, cell counts and leading cell titles of a Jupyter
/// notebook. Invalid input is <c>Valid = false</c> with an error code and position.
/// </summary>
/// <remarks>
/// The notebook is read through the bounded OfficeIMO reader from the caller's bytes: no kernel runs,
/// stored output payloads are not decoded, and widgets and remote resources are never loaded. Facts the
/// reader does not expose are omitted rather than guessed.
/// </remarks>
public sealed class NotebookMetadataPlugin : IMediaMetadataCapability
{
    private const string IpynbFormat = "ipynb";

    private readonly NotebookPluginOptions options;

    public NotebookMetadataPlugin()
        : this(NotebookPluginOptions.Default)
    {
    }

    public NotebookMetadataPlugin(NotebookPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(NotebookSchema.Extensions, NotebookSchema.ContentTypes, NotebookSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bytes = await NotebookSourceReader
            .ReadAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.4));

        var facts = NotebookDocumentReader.Read(bytes, options, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(ToValues(facts));
    }

    private List<MediaMetadataValue> ToValues(NotebookFacts facts)
    {
        var values = new List<MediaMetadataValue>
        {
            new(NotebookSchema.NotebookFormat, MediaMetadataType.Text, Text: IpynbFormat),
            new(NotebookSchema.Valid, MediaMetadataType.Boolean, Boolean: facts.Valid),
            new(NotebookSchema.ByteLength, MediaMetadataType.Integer, Integer: facts.ByteLength)
        };

        if (!facts.Valid)
        {
            AddText(values, NotebookSchema.ErrorCode, facts.ErrorCode);
            AddInteger(values, NotebookSchema.ErrorLine, facts.ErrorLine);
            AddInteger(values, NotebookSchema.ErrorColumn, facts.ErrorColumn);
            return values;
        }

        AddText(values, NotebookSchema.NbFormatVersion, facts.NbFormatVersion);
        AddText(values, NotebookSchema.Language, facts.Language);
        AddInteger(values, NotebookSchema.CellCount, facts.CellCount);
        AddInteger(values, NotebookSchema.IncludedCellCount, facts.IncludedCellCount);
        AddText(values, NotebookSchema.CellTitles, Join(facts.CellTitles));
        return values;
    }

    private void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var value = text.Trim();
        if (value.Length > options.MaxMetadataValueCharacters)
        {
            value = value[..options.MaxMetadataValueCharacters];
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
    }

    private static void AddInteger(List<MediaMetadataValue> values, string key, int? number)
    {
        if (number is { } value)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: value));
        }
    }

    private static string? Join(IReadOnlyList<string> titles) =>
        titles.Count == 0 ? null : string.Join(", ", titles);
}
