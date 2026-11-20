using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.OpenOffice.Exceptions;

namespace SkyCD.Plugin.OpenOffice;

/// <summary>The <c>office:version</c> attribute and the metadata values parsed from <c>meta.xml</c>.</summary>
internal sealed record OpenOfficeMetadata(string? FormatVersion, IReadOnlyList<MediaMetadataValue> Values);

/// <summary>
/// Parses the <c>office:meta</c> element of an OpenDocument or OpenOffice.org metadata member.
/// </summary>
/// <remarks>
/// Elements are matched by namespace and local name, so the OpenDocument 1.2 namespaces and the
/// <c>http://openoffice.org/2000/</c> families used by OpenDocument 1.0/1.1 and OpenOffice.org 1.x are both
/// read. A field the document does not carry is omitted rather than published as an empty string, so
/// "unknown" stays distinguishable from "none". Only direct text of an element is used, which keeps a
/// template's nested date from being mistaken for its name.
/// </remarks>
internal static class OpenOfficeMetaReader
{
    private static readonly IReadOnlyDictionary<string, string> DublinCoreTextFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["title"] = OpenOfficeSchema.Title,
            ["subject"] = OpenOfficeSchema.Subject,
            ["description"] = OpenOfficeSchema.Description,
            ["creator"] = OpenOfficeSchema.Author,
            ["language"] = OpenOfficeSchema.Language,
            ["rights"] = OpenOfficeSchema.Rights,
            ["publisher"] = OpenOfficeSchema.Publisher,
            ["contributor"] = OpenOfficeSchema.Contributor,
            ["identifier"] = OpenOfficeSchema.Identifier,
            ["source"] = OpenOfficeSchema.Source,
            ["relation"] = OpenOfficeSchema.Relation,
            ["coverage"] = OpenOfficeSchema.Coverage,
            ["type"] = OpenOfficeSchema.Type,
            ["format"] = OpenOfficeSchema.Format
        };

    private static readonly IReadOnlyDictionary<string, string> MetaTextFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["initial-creator"] = OpenOfficeSchema.InitialCreator,
            ["generator"] = OpenOfficeSchema.Generator,
            ["printed-by"] = OpenOfficeSchema.PrintedBy,
            ["template"] = OpenOfficeSchema.Template
        };

    private static readonly IReadOnlyDictionary<string, string> MetaDateFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["creation-date"] = OpenOfficeSchema.CreatedAt,
            ["print-date"] = OpenOfficeSchema.PrintedAt
        };

    private static readonly IReadOnlyDictionary<string, string> StatisticFields =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["page-count"] = OpenOfficeSchema.PageCount,
            ["table-count"] = OpenOfficeSchema.TableCount,
            ["image-count"] = OpenOfficeSchema.ImageCount,
            ["object-count"] = OpenOfficeSchema.ObjectCount,
            ["paragraph-count"] = OpenOfficeSchema.ParagraphCount,
            ["word-count"] = OpenOfficeSchema.WordCount,
            ["character-count"] = OpenOfficeSchema.CharacterCount,
            ["cell-count"] = OpenOfficeSchema.CellCount,
            ["sheet-count"] = OpenOfficeSchema.SheetCount
        };

    public static OpenOfficeMetadata Read(byte[] metadataEntry, int maxCharacters)
    {
        XDocument document;
        try
        {
            using var stream = new MemoryStream(metadataEntry, writable: false);
            using var reader = XmlReader.Create(stream, OpenOfficeXml.CreateSettings(maxCharacters));
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new OpenOfficeDecodeException("the metadata entry is not well-formed XML", exception);
        }

        var root = document.Root ?? throw new OpenOfficeDecodeException("the metadata entry is empty");
        if (!OpenOfficeNamespaces.IsOffice(root.Name) ||
            !string.Equals(root.Name.LocalName, "document-meta", StringComparison.Ordinal))
        {
            throw new OpenOfficeDecodeException("the metadata entry is not an OpenDocument metadata document");
        }

        var formatVersion = root.Attributes()
            .Where(attribute => OpenOfficeNamespaces.IsOffice(attribute.Name) &&
                                string.Equals(attribute.Name.LocalName, "version", StringComparison.Ordinal))
            .Select(attribute => attribute.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);

        var meta = root.Elements().FirstOrDefault(element =>
            OpenOfficeNamespaces.IsOffice(element.Name) &&
            string.Equals(element.Name.LocalName, "meta", StringComparison.Ordinal));

        var values = new List<MediaMetadataValue>();
        if (meta is not null)
        {
            ReadMeta(meta, values);
        }

        return new OpenOfficeMetadata(formatVersion, values);
    }

    private static void ReadMeta(XElement meta, List<MediaMetadataValue> values)
    {
        var published = new HashSet<string>(StringComparer.Ordinal);
        var keywords = new List<string>();

        foreach (var element in meta.Elements())
        {
            if (OpenOfficeNamespaces.IsDublinCore(element.Name))
            {
                ReadDublinCoreElement(element, values, published);
                continue;
            }

            if (!OpenOfficeNamespaces.IsMeta(element.Name))
            {
                continue;
            }

            ReadMetaElement(element, values, published, keywords);
        }

        if (keywords.Count > 0 && published.Add(OpenOfficeSchema.Keywords))
        {
            values.Add(new MediaMetadataValue(OpenOfficeSchema.Keywords, MediaMetadataType.Text,
                Text: string.Join(", ", keywords)));
        }
    }

    private static void ReadDublinCoreElement(
        XElement element,
        List<MediaMetadataValue> values,
        HashSet<string> published)
    {
        if (DublinCoreTextFields.TryGetValue(element.Name.LocalName, out var key))
        {
            AddText(values, published, key, DirectText(element));
        }
        else if (string.Equals(element.Name.LocalName, "date", StringComparison.Ordinal))
        {
            AddTimestamp(values, published, OpenOfficeSchema.ModifiedAt, ParseTimestamp(DirectText(element)));
        }
    }

    private static void ReadMetaElement(
        XElement element,
        List<MediaMetadataValue> values,
        HashSet<string> published,
        List<string> keywords)
    {
        var localName = element.Name.LocalName;
        if (MetaTextFields.TryGetValue(localName, out var textKey))
        {
            AddText(values, published, textKey, DirectText(element));
        }
        else if (string.Equals(localName, "keyword", StringComparison.Ordinal))
        {
            var keyword = DirectText(element).Trim();
            if (keyword.Length > 0)
            {
                keywords.Add(keyword);
            }
        }
        else if (MetaDateFields.TryGetValue(localName, out var dateKey))
        {
            AddTimestamp(values, published, dateKey, ParseTimestamp(DirectText(element)));
        }
        else if (string.Equals(localName, "editing-cycles", StringComparison.Ordinal))
        {
            AddInteger(values, published, OpenOfficeSchema.EditingCycles,
                ParseNonNegativeInteger(DirectText(element)));
        }
        else if (string.Equals(localName, "editing-duration", StringComparison.Ordinal))
        {
            AddInteger(values, published, OpenOfficeSchema.EditingTimeMinutes,
                ParseEditingMinutes(DirectText(element)));
        }
        else if (string.Equals(localName, "document-statistic", StringComparison.Ordinal))
        {
            ReadStatistics(element, values, published);
        }
    }

    private static void ReadStatistics(
        XElement statistic,
        List<MediaMetadataValue> values,
        HashSet<string> published)
    {
        foreach (var attribute in statistic.Attributes())
        {
            if (OpenOfficeNamespaces.IsMeta(attribute.Name) &&
                StatisticFields.TryGetValue(attribute.Name.LocalName, out var key))
            {
                AddInteger(values, published, key, ParseNonNegativeInteger(attribute.Value));
            }
        }
    }

    /// <summary>The element's own text nodes only; nested markup such as a template's date is not merged in.</summary>
    private static string DirectText(XElement element) =>
        string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value));

    private static void AddText(
        List<MediaMetadataValue> values,
        HashSet<string> published,
        string key,
        string text)
    {
        var value = text.Trim();
        if (value.Length == 0 || !published.Add(key))
        {
            return;
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
    }

    private static void AddTimestamp(
        List<MediaMetadataValue> values,
        HashSet<string> published,
        string key,
        DateTimeOffset? timestamp)
    {
        if (timestamp is not { } value || !published.Add(key))
        {
            return;
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: value));
    }

    private static void AddInteger(
        List<MediaMetadataValue> values,
        HashSet<string> published,
        string key,
        long? number)
    {
        if (number is not { } value || !published.Add(key))
        {
            return;
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: value));
    }

    private static long? ParseNonNegativeInteger(string text) =>
        long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;

    private static long? ParseEditingMinutes(string text)
    {
        try
        {
            var duration = XmlConvert.ToTimeSpan(text.Trim());
            return duration < TimeSpan.Zero ? null : (long)duration.TotalMinutes;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parses an XML Schema <c>dateTime</c>. A value without a UTC offset is published as a nominal time with
    /// a zero offset rather than being shifted by the machine's current time zone, so the same file always
    /// yields the same value.
    /// </summary>
    private static DateTimeOffset? ParseTimestamp(string text)
    {
        var value = text.Trim();
        if (value.Length == 0)
        {
            return null;
        }

        try
        {
            if (HasUtcOffset(value))
            {
                return XmlConvert.ToDateTimeOffset(value);
            }

            var nominal = XmlConvert.ToDateTime(value, XmlDateTimeSerializationMode.Unspecified);
            return new DateTimeOffset(nominal, TimeSpan.Zero);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool HasUtcOffset(string value)
    {
        if (value.EndsWith('Z'))
        {
            return true;
        }

        if (value.Length < 6)
        {
            return false;
        }

        return value[^6] is '+' or '-' &&
               char.IsAsciiDigit(value[^5]) &&
               char.IsAsciiDigit(value[^4]) &&
               value[^3] == ':' &&
               char.IsAsciiDigit(value[^2]) &&
               char.IsAsciiDigit(value[^1]);
    }
}
