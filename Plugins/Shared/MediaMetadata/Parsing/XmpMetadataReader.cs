using System;
using System.Collections.Generic;
using System.Globalization;
using XmpCore;

namespace SkyCD.Plugin.MediaMetadata.Parsing;

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
/// <c>xmp:Rating</c> and their Microsoft/IPTC counterparts. Parsing is delegated to XmpCore, the XMP
/// toolkit MetadataExtractor already uses, so hostile packets never reach a hand-written XML reader.
/// </summary>
internal static class XmpMetadataReader
{
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string XmpNamespace = "http://ns.adobe.com/xap/1.0/";
    private const string ExifNamespace = "http://ns.adobe.com/exif/1.0/";
    private const string PdfNamespace = "http://ns.adobe.com/pdf/1.3/";
    private const string MicrosoftPhotoNamespace = "http://ns.microsoft.com/photo/1.0/";
    private const string DefaultLanguage = "x-default";

    /// <summary>Parses one located packet; a malformed packet yields no metadata rather than an exception.</summary>
    public static bool TryRead(ReadOnlySpan<byte> packet, out XmpMetadata metadata)
    {
        metadata = XmpMetadata.Empty;
        if (packet.IsEmpty)
        {
            return false;
        }

        IXmpMeta parsed;
        try
        {
            parsed = XmpMetaFactory.ParseFromBuffer(packet.ToArray());
        }
        catch (Exception exception) when (exception is XmpException or ArgumentException)
        {
            return false;
        }

        return TryRead(parsed, out metadata);
    }

    /// <summary>Reads the portable properties from a packet the container reader already parsed.</summary>
    public static bool TryRead(IXmpMeta? meta, out XmpMetadata metadata)
    {
        metadata = XmpMetadata.Empty;
        if (meta is null)
        {
            return false;
        }

        try
        {
            metadata = new XmpMetadata(ReadKeywords(meta), ReadComment(meta), ReadRating(meta));
            return true;
        }
        catch (XmpException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ReadKeywords(IXmpMeta meta)
    {
        var keywords = new List<string>();
        var count = meta.CountArrayItems(DcNamespace, "subject");
        for (var index = 1; index <= count; index++)
        {
            AddKeyword(keywords, meta.GetArrayItem(DcNamespace, "subject", index)?.Value);
        }

        foreach (var keyword in SplitDelimited(ReadValue(meta, PdfNamespace, "Keywords")))
        {
            AddKeyword(keywords, keyword);
        }

        return keywords;
    }

    private static void AddKeyword(List<string> keywords, string? value)
    {
        var normalized = MetadataText.Clean(value);
        if (normalized.Length > 0)
        {
            keywords.Add(normalized);
        }
    }

    /// <summary>Alt text prefers the <c>x-default</c> language, matching how tools present captions.</summary>
    private static string? ReadComment(IXmpMeta meta)
    {
        var localized = MetadataText.Clean(
            meta.GetLocalizedText(DcNamespace, "description", null, DefaultLanguage)?.Value);
        if (localized.Length > 0)
        {
            return localized;
        }

        var userComment = MetadataText.Clean(ReadValue(meta, ExifNamespace, "UserComment"));
        return userComment.Length > 0 ? userComment : null;
    }

    private static IReadOnlyList<string> SplitDelimited(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// <c>xmp:Rating</c> uses a 0–5 star scale. The Microsoft namespace persists 0–99 (or -1 for rejected),
    /// which <see cref="MetadataScan.NormalizeRating"/> converts to stars.
    /// </summary>
    private static int? ReadRating(IXmpMeta meta)
    {
        var standard = ParseInteger(ReadValue(meta, XmpNamespace, "Rating"));
        if (standard is >= 0)
        {
            return standard;
        }

        var microsoft = ParseInteger(ReadValue(meta, MicrosoftPhotoNamespace, "Rating"));
        return microsoft is >= 0 ? microsoft : null;
    }

    private static int? ParseInteger(string? value) =>
        int.TryParse(MetadataText.Clean(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>A missing property or an unrecognized namespace yields no value instead of failing the read.</summary>
    private static string? ReadValue(IXmpMeta meta, string schemaNamespace, string propertyName)
    {
        try
        {
            return meta.GetProperty(schemaNamespace, propertyName)?.Value;
        }
        catch (XmpException)
        {
            return null;
        }
    }
}
