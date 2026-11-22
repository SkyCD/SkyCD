using System;
using System.Collections.Generic;
using System.Text;

namespace SkyCD.Plugin.Metadata.Xmp.Tests;

/// <summary>Builds an in-memory XMP packet so the reader runs against a real packet layout.</summary>
internal static class XmpFixtures
{
    public static string Xmp(
        IReadOnlyList<string>? keywords = null,
        string? description = null,
        int? rating = null,
        int? microsoftRating = null,
        string? pdfKeywords = null)
    {
        var builder = new StringBuilder();
        builder.Append("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>");
        builder.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">");
        builder.Append("<rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">");
        builder.Append("<rdf:Description rdf:about=\"\"");
        builder.Append(" xmlns:dc=\"http://purl.org/dc/elements/1.1/\"");
        builder.Append(" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\"");
        builder.Append(" xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\"");
        builder.Append(" xmlns:MicrosoftPhoto=\"http://ns.microsoft.com/photo/1.0/\">");

        if (keywords is not null)
        {
            builder.Append("<dc:subject><rdf:Bag>");
            foreach (var keyword in keywords)
            {
                builder.Append("<rdf:li>").Append(Escape(keyword)).Append("</rdf:li>");
            }

            builder.Append("</rdf:Bag></dc:subject>");
        }

        if (description is not null)
        {
            builder.Append("<dc:description><rdf:Alt>");
            builder.Append("<rdf:li xml:lang=\"x-default\">").Append(Escape(description)).Append("</rdf:li>");
            builder.Append("</rdf:Alt></dc:description>");
        }

        if (rating is not null)
        {
            builder.Append("<xmp:Rating>").Append(rating.Value).Append("</xmp:Rating>");
        }

        if (microsoftRating is not null)
        {
            builder.Append("<MicrosoftPhoto:Rating>").Append(microsoftRating.Value)
                .Append("</MicrosoftPhoto:Rating>");
        }

        if (pdfKeywords is not null)
        {
            builder.Append("<pdf:Keywords>").Append(Escape(pdfKeywords)).Append("</pdf:Keywords>");
        }

        builder.Append("</rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end=\"w\"?>");
        return builder.ToString();
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
