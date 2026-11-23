using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.Metadata.AbiWord.Exceptions;

namespace SkyCD.Plugin.Metadata.AbiWord.Parsing;

/// <summary>
/// Recovers a bounded leading text sample from decompressed AbiWord XML.
/// </summary>
/// <remarks>
/// The walk reads the same AWML tree the metadata pass accepts, but stops as soon as the character cap or the
/// paragraph cap is reached, so a large body costs no more than the sample it produces. Each paragraph's own
/// runs are read into a buffer first so an empty paragraph is dropped instead of contributing a blank line.
/// Everything outside <c>abiword/section/p</c> is skipped: tables, lists, endnotes, headers and footers live in
/// sibling elements of their own, and nested paragraphs inside them are not walked.
/// </remarks>
internal static class AbiWordBodyTextReader
{
    private const string NamespaceUri = "http://www.abisource.com/awml.dtd";
    private const int MaxXmlDepth = 64;

    /// <summary>
    /// Returns the leading paragraph text, joined by line breaks, or an empty string when the body carries no
    /// text. Never throws for malformed XML: the text recovered so far is the result.
    /// </summary>
    public static string Read(
        ReadOnlyMemory<byte> data,
        int maxCharacters,
        int maxParagraphs,
        int maxDecompressedBytes,
        CancellationToken cancellationToken)
    {
        if (maxCharacters <= 0 || maxParagraphs <= 0 || data.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            return ReadCore(data, maxCharacters, maxParagraphs, maxDecompressedBytes, cancellationToken);
        }
        catch (XmlException)
        {
            return string.Empty;
        }
    }

    private static string ReadCore(
        ReadOnlyMemory<byte> data,
        int maxCharacters,
        int maxParagraphs,
        int maxDecompressedBytes,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data.ToArray(), writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            // AbiWord writes a DOCTYPE; ignore it rather than resolving or expanding its entities.
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = maxDecompressedBytes,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        });

        if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "abiword" ||
            (reader.NamespaceURI.Length != 0 && reader.NamespaceURI != NamespaceUri))
        {
            return string.Empty;
        }

        var documentNamespace = reader.NamespaceURI;
        var text = new StringBuilder(Math.Min(maxCharacters, 4096));
        var paragraphs = 0;
        var inBody = false;

        while (reader.Read() && text.Length < maxCharacters)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateNamespaces(reader, documentNamespace);

            if (reader.Depth == 1)
            {
                inBody = reader.NodeType == XmlNodeType.Element && reader.LocalName == "section" &&
                         reader.NamespaceURI == documentNamespace && !reader.IsEmptyElement;
                continue;
            }

            if (!inBody || reader.Depth != 2 || reader.NodeType != XmlNodeType.Element ||
                reader.LocalName != "p" || reader.NamespaceURI != documentNamespace)
            {
                continue;
            }

            if (paragraphs >= maxParagraphs)
            {
                break;
            }

            paragraphs++;
            if (reader.IsEmptyElement)
            {
                continue;
            }

            // One extra character beyond the cap tells a trimmed paragraph apart from an oversized one.
            var content = ReadParagraphContent(reader, documentNamespace, maxCharacters + 1, cancellationToken);
            if (content.Length == 0)
            {
                continue;
            }

            if (text.Length > 0)
            {
                text.Append('\n');
            }

            text.Append(content, 0, Math.Min(content.Length, maxCharacters - text.Length));
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads the character runs of one paragraph until its end. Formatting elements are entered, while a block
    /// nested inside a paragraph, such as a table, contributes nothing: its own paragraphs are not the text of
    /// this one.
    /// </summary>
    private static string ReadParagraphContent(
        XmlReader reader,
        string documentNamespace,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder();
        var depth = reader.Depth;
        while (!reader.EOF)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
            {
                break;
            }

            ValidateNamespaces(reader, documentNamespace);

            if (reader.Depth > depth && reader.NodeType == XmlNodeType.Element && IsBlockElement(reader.LocalName))
            {
                reader.Skip();
                continue;
            }

            if (reader.NodeType == XmlNodeType.Text && reader.Depth > depth &&
                content.Length < maxCharacters)
            {
                var take = Math.Min(reader.Value.Length, maxCharacters - content.Length);
                content.Append(reader.Value, 0, take);
            }

            reader.Read();
        }

        return content.ToString().Trim();
    }

    private static bool IsBlockElement(string localName) =>
        localName is "table" or "cell" or "p" or "section" or "list" or "listitem";

    /// <summary>Rejects stray elements from another namespace so a foreign body cannot be drawn.</summary>
    private static void ValidateNamespaces(XmlReader reader, string documentNamespace)
    {
        if (reader.Depth > MaxXmlDepth)
        {
            throw new AbiWordLimitExceededException("XML depth", MaxXmlDepth);
        }

        if (reader.NodeType is XmlNodeType.Element or XmlNodeType.EndElement && reader.Depth > 0 &&
            reader.NamespaceURI != documentNamespace)
        {
            throw new AbiWordDecodeException("the body mixes namespaces");
        }
    }
}
