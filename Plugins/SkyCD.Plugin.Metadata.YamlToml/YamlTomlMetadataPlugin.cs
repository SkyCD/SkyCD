using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.YamlToml.Models;
using SkyCD.Plugin.Metadata.YamlToml.Parsing;

namespace SkyCD.Plugin.Metadata.YamlToml;

/// <summary>
/// Publishes the structure of a YAML or TOML document. Invalid input is <c>Valid = false</c> with an error
/// code and position, and no invented tree.
/// </summary>
public sealed class YamlTomlMetadataPlugin : IMediaMetadataCapability
{
    private readonly YamlTomlPluginOptions options;

    public YamlTomlMetadataPlugin()
        : this(YamlTomlPluginOptions.Default)
    {
    }

    public YamlTomlMetadataPlugin(YamlTomlPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(YamlTomlSchema.Extensions, YamlTomlSchema.ContentTypes, YamlTomlSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var bytes = await YamlTomlSourceReader
            .ReadAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);
        progress?.Report(new MediaProgress(0.4));

        var format = YamlTomlDocumentReader.ResolveFormat(item.Extension, item.ContentType);
        var facts = bytes.Length == 0
            ? new YamlTomlFacts
            {
                Format = format,
                Valid = false,
                ByteLength = 0,
                ErrorCode = "empty"
            }
            : YamlTomlDocumentReader.Read(bytes, format, options, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(ToValues(facts));
    }

    private List<MediaMetadataValue> ToValues(YamlTomlFacts facts)
    {
        var values = new List<MediaMetadataValue>
        {
            new(YamlTomlSchema.Format, MediaMetadataType.Text, Text: facts.Format),
            new(YamlTomlSchema.Valid, MediaMetadataType.Boolean, Boolean: facts.Valid),
            new(YamlTomlSchema.ByteLength, MediaMetadataType.Integer, Integer: facts.ByteLength)
        };

        if (!facts.Valid)
        {
            AddText(values, YamlTomlSchema.ErrorCode, facts.ErrorCode);
            AddInteger(values, YamlTomlSchema.ErrorLine, facts.ErrorLine);
            AddInteger(values, YamlTomlSchema.ErrorColumn, facts.ErrorColumn);
            return values;
        }

        if (facts.Format == "toml")
        {
            values.Add(new MediaMetadataValue(YamlTomlSchema.TableCount, MediaMetadataType.Integer,
                Integer: facts.TableCount));
            values.Add(new MediaMetadataValue(YamlTomlSchema.ArrayOfTablesCount, MediaMetadataType.Integer,
                Integer: facts.ArrayOfTablesCount));
            values.Add(new MediaMetadataValue(YamlTomlSchema.KeyCount, MediaMetadataType.Integer,
                Integer: facts.KeyCount));
            values.Add(new MediaMetadataValue(YamlTomlSchema.MaxDepth, MediaMetadataType.Integer,
                Integer: facts.MaxDepth));
            values.Add(new MediaMetadataValue(YamlTomlSchema.CommentCount, MediaMetadataType.Integer,
                Integer: facts.CommentCount));
            values.Add(new MediaMetadataValue(YamlTomlSchema.DuplicateKeyCount, MediaMetadataType.Integer,
                Integer: facts.DuplicateKeyCount));
            AddText(values, YamlTomlSchema.TopLevelTables, Join(facts.TopLevelTables));
            return values;
        }

        AddText(values, YamlTomlSchema.FormatVersion, facts.FormatVersion);
        values.Add(new MediaMetadataValue(YamlTomlSchema.DocumentCount, MediaMetadataType.Integer,
            Integer: facts.DocumentCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.MappingCount, MediaMetadataType.Integer,
            Integer: facts.MappingCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.SequenceCount, MediaMetadataType.Integer,
            Integer: facts.SequenceCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.ScalarCount, MediaMetadataType.Integer,
            Integer: facts.ScalarCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.MaxDepth, MediaMetadataType.Integer,
            Integer: facts.MaxDepth));
        values.Add(new MediaMetadataValue(YamlTomlSchema.AnchorCount, MediaMetadataType.Integer,
            Integer: facts.AnchorCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.AliasCount, MediaMetadataType.Integer,
            Integer: facts.AliasCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.MergeKeyCount, MediaMetadataType.Integer,
            Integer: facts.MergeKeyCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.CommentCount, MediaMetadataType.Integer,
            Integer: facts.CommentCount));
        values.Add(new MediaMetadataValue(YamlTomlSchema.DuplicateKeyCount, MediaMetadataType.Integer,
            Integer: facts.DuplicateKeyCount));
        AddText(values, YamlTomlSchema.TopLevelKeys, Join(facts.TopLevelKeys));
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

    private string? Join(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
        {
            return null;
        }

        return string.Join(", ", names);
    }
}
