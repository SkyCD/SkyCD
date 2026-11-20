using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.AbiWord.Exceptions;

namespace SkyCD.Plugin.AbiWord;

public sealed class AbiWordMetadataPlugin : IMediaMetadataCapability
{
    private const string NamespaceUri = "http://www.abisource.com/awml.dtd";
    private const int MaxXmlDepth = 64;
    private readonly AbiWordPluginOptions options;

    private static readonly IReadOnlyDictionary<string, string> MetadataKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["dc.title"] = AbiWordSchema.Title,
        ["dc.creator"] = AbiWordSchema.Author,
        ["dc.subject"] = AbiWordSchema.Subject,
        ["dc.description"] = AbiWordSchema.Description,
        ["abiword.keywords"] = AbiWordSchema.Keywords,
        ["dc.language"] = AbiWordSchema.Language,
        ["dc.publisher"] = AbiWordSchema.Publisher,
        ["dc.contributor"] = AbiWordSchema.Contributor,
        ["dc.rights"] = AbiWordSchema.Rights,
        ["dc.type"] = AbiWordSchema.Type,
        ["dc.source"] = AbiWordSchema.Source,
        ["dc.relation"] = AbiWordSchema.Relation,
        ["dc.identifier"] = AbiWordSchema.Identifier,
        ["dc.coverage"] = AbiWordSchema.Coverage,
        ["dc.date"] = AbiWordSchema.Date,
        ["abiword.date_last_changed"] = AbiWordSchema.LastChanged,
        ["dc.format"] = AbiWordSchema.Format,
        ["abiword.generator"] = AbiWordSchema.Generator
    };

    public AbiWordMetadataPlugin() : this(AbiWordPluginOptions.Default)
    {
    }

    public AbiWordMetadataPlugin(AbiWordPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor => new(AbiWordSchema.Extensions, AbiWordSchema.ContentTypes, AbiWordSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item, IMediaSource source, IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        var bytes = await AbiWordSourceReader.ReadAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.5));
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                // AbiWord writes a DOCTYPE; ignore it rather than resolving or expanding its entities.
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersInDocument = options.MaxDecompressedBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            });
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "abiword" ||
                (reader.NamespaceURI.Length != 0 && reader.NamespaceURI != NamespaceUri))
            {
                throw new AbiWordDecodeException("the root is not AbiWord XML");
            }

            var documentNamespace = reader.NamespaceURI;
            var values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AbiWordSchema.Container] = "abw"
            };
            AddText(values, AbiWordSchema.FormatVersion, reader.GetAttribute("version"));
            var inMetadata = false;
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > MaxXmlDepth)
                {
                    throw new AbiWordLimitExceededException("XML depth", MaxXmlDepth);
                }

                if (reader.Depth == 1)
                {
                    inMetadata = reader.NodeType == XmlNodeType.Element && reader.LocalName == "metadata" &&
                                 reader.NamespaceURI == documentNamespace && !reader.IsEmptyElement;
                }

                if (!inMetadata || reader.Depth != 2 || reader.NodeType != XmlNodeType.Element ||
                    reader.LocalName != "m" || reader.NamespaceURI != documentNamespace)
                {
                    continue;
                }

                var key = reader.GetAttribute("key");
                if (key is not null && MetadataKeys.TryGetValue(key, out var field))
                {
                    AddText(values, field, ReadMetadataText(reader, cancellationToken));
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new MediaProgress(1));
            return new MediaExtractionResult(values.Select(pair =>
                new MediaMetadataValue(pair.Key, MediaMetadataType.Text, Text: pair.Value)).ToArray());
        }
        catch (XmlException exception)
        {
            throw new AbiWordDecodeException("invalid or unsupported XML", exception);
        }
    }

    private string ReadMetadataText(XmlReader reader, CancellationToken cancellationToken)
    {
        using var element = reader.ReadSubtree();
        var text = new StringBuilder();
        var buffer = new char[1024];
        while (element.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (element.NodeType == XmlNodeType.Element && element.Depth > 0)
            {
                throw new AbiWordDecodeException("metadata contains nested markup");
            }

            if (element.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
            {
                continue;
            }

            int read;
            while ((read = element.ReadValueChunk(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((long)text.Length + read > options.MaxMetadataCharacters)
                {
                    throw new AbiWordLimitExceededException("metadata field characters", options.MaxMetadataCharacters);
                }

                text.Append(buffer, 0, read);
            }
        }

        return text.ToString();
    }

    private void AddText(Dictionary<string, string> values, string key, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (text.Length > options.MaxMetadataCharacters)
        {
            throw new AbiWordLimitExceededException("metadata field characters", options.MaxMetadataCharacters);
        }

        values[key] = text.Trim();
    }
}
