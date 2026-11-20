using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SkyCD.Plugin.MediaMetadata;

/// <summary>Portable properties read from one XMP packet.</summary>
internal sealed record XmpMetadata(
    IReadOnlyList<string> Keywords,
    string? Comment,
    int? Rating)
{
    public static XmpMetadata Empty { get; } = new([], null, null);
}

/// <summary>
/// Reads the properties that file managers agree on: <c>dc:subject</c>, <c>dc:description</c>,
/// <c>xmp:Rating</c> and their Microsoft/IPTC counterparts. The XML parser is configured without DTD
/// processing or an external resolver, and the packet was already bounded by the caller, so a hostile file
/// can waste at most one bounded parse.
/// </summary>
internal static class XmpMetadataReader
{
    private static readonly XNamespace Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private static readonly XNamespace Xml = XNamespace.Xml;
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xmp = "http://ns.adobe.com/xap/1.0/";
    private static readonly XNamespace Exif = "http://ns.adobe.com/exif/1.0/";
    private static readonly XNamespace Pdf = "http://ns.adobe.com/pdf/1.3/";
    private static readonly XNamespace MicrosoftPhoto = "http://ns.microsoft.com/photo/1.0/";

    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        IgnoreWhitespace = false
    };

    private static readonly UTF8Encoding LenientUtf8 = new(encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: false);

    /// <summary>Parses one located packet; a malformed packet yields no metadata rather than an exception.</summary>
    public static bool TryRead(ReadOnlySpan<byte> packet, out XmpMetadata metadata)
    {
        metadata = XmpMetadata.Empty;
        if (packet.IsEmpty)
        {
            return false;
        }

        XDocument document;
        try
        {
            var text = LenientUtf8.GetString(packet);
            using var stringReader = new StringReader(text);
            using var xmlReader = XmlReader.Create(stringReader, ReaderSettings);
            document = XDocument.Load(xmlReader);
        }
        catch (Exception exception) when (exception is XmlException or ArgumentException or InvalidOperationException)
        {
            return false;
        }

        var keywords = new List<string>();
        foreach (var property in Find(document, Dc, "subject"))
        {
            keywords.AddRange(ReadList(property));
        }

        foreach (var property in Find(document, Pdf, "Keywords"))
        {
            keywords.AddRange(SplitDelimited(property.Value));
        }

        var comment = Find(document, Dc, "description")
            .Select(ReadLocalized)
            .FirstOrDefault(value => value.Length > 0);
        if (string.IsNullOrEmpty(comment))
        {
            comment = Find(document, Exif, "UserComment")
                .Select(property => MetadataText.Clean(property.Value))
                .FirstOrDefault(value => value.Length > 0);
        }

        metadata = new XmpMetadata(keywords, comment, ReadRating(document));
        return true;
    }

    private static IEnumerable<XElement> Find(XContainer document, XNamespace ns, string localName) =>
        document.Descendants().Where(element => element.Name == ns + localName);

    private static IReadOnlyList<string> ReadList(XElement property)
    {
        var values = property.Descendants(Rdf + "li")
            .Select(item => MetadataText.Clean(item.Value))
            .Where(value => value.Length > 0)
            .ToList();
        if (values.Count > 0)
        {
            return values;
        }

        if (property.Attribute(Rdf + "resource") is not null ||
            string.Equals((string?)property.Attribute(Rdf + "parseType"), "Resource", StringComparison.Ordinal))
        {
            return values;
        }

        var direct = MetadataText.Clean(property.Value);
        if (direct.Length > 0)
        {
            values.Add(direct);
        }

        return values;
    }

    /// <summary>Alt text prefers the <c>x-default</c> language, matching how tools present captions.</summary>
    private static string ReadLocalized(XElement property)
    {
        var items = property.Descendants(Rdf + "li").ToList();
        if (items.Count == 0)
        {
            return MetadataText.Clean(property.Value);
        }

        var preferred = items.FirstOrDefault(item =>
                             string.Equals((string?)item.Attribute(Xml + "lang"), "x-default",
                                 StringComparison.OrdinalIgnoreCase))
                         ?? items[0];
        return MetadataText.Clean(preferred.Value);
    }

    private static IReadOnlyList<string> SplitDelimited(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(MetadataText.Clean)
                .Where(text => text.Length > 0)
                .ToArray();

    /// <summary>
    /// <c>xmp:Rating</c> uses a 0–5 star scale. The Microsoft namespace persists 0–99 (or -1 for rejected),
    /// which <see cref="MetadataScan.NormalizeRating"/> converts to stars.
    /// </summary>
    private static int? ReadRating(XContainer document)
    {
        var standard = FirstInteger(Find(document, Xmp, "Rating"));
        if (standard is >= 0)
        {
            return standard;
        }

        var microsoft = FirstInteger(Find(document, MicrosoftPhoto, "Rating"));
        return microsoft is >= 0 ? microsoft : null;
    }

    private static int? FirstInteger(IEnumerable<XElement> properties)
    {
        foreach (var property in properties)
        {
            if (int.TryParse(MetadataText.Clean(property.Value), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }
}
