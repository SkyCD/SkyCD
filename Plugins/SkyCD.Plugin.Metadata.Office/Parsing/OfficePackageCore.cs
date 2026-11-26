using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads the core properties an Open Packaging Convention package stores in its core part.
/// </summary>
/// <remarks>
/// The part is located through the already-open package, so a producer that keeps it under
/// <c>package/services/metadata/core-properties</c> reads the same as one that names it
/// <c>docProps/core.xml</c>, and nothing outside the document is ever resolved.
/// <para>
/// The part's own XML tree is read rather than the typed element the packaging library exposes: that element
/// stays null for a core part reached through a read-only document, which would silently cost every core field
/// while leaving the rest of the document intact. Element names are matched on their local name only, because
/// producers differ on the prefixes they declare for the same namespaces. Entities and external resolution are
/// refused, and a core part that cannot be parsed costs the core fields, not the document.
/// </para>
/// </remarks>
internal static class OfficePackageCore
{
    public static void Apply(OfficeMetadata metadata, Func<CoreFilePropertiesPart?> read)
    {
        XElement? root;
        try
        {
            var part = read();
            root = part is null ? null : Load(part);
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
                                              or XmlException)
        {
            return;
        }

        if (root is null)
        {
            return;
        }

        metadata.Title = OfficeText.Normalize(Text(root, "title"));
        metadata.Subject = OfficeText.Normalize(Text(root, "subject"));
        metadata.Author = OfficeText.Normalize(Text(root, "creator"));
        metadata.Keywords = OfficeText.Normalize(Text(root, "keywords"));
        metadata.Comments = OfficeText.Normalize(Text(root, "description"));
        metadata.Category = OfficeText.Normalize(Text(root, "category"));
        metadata.ContentStatus = OfficeText.Normalize(Text(root, "contentStatus"));
        metadata.LastSavedBy = OfficeText.Normalize(Text(root, "lastModifiedBy"));
        metadata.RevisionNumber = OfficeText.Normalize(Text(root, "revision"));
        metadata.Language = OfficeText.Normalize(Text(root, "language"));
        metadata.CreatedAt = OfficeText.ParseTimestamp(Text(root, "created"));
        metadata.ModifiedAt = OfficeText.ParseTimestamp(Text(root, "modified"));
        metadata.LastPrintedAt = OfficeText.ParseTimestamp(Text(root, "lastPrinted"));
    }

    private static XElement Load(CoreFilePropertiesPart part)
    {
        using var stream = part.GetStream();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        });

        return XElement.Load(reader);
    }

    private static string? Text(XElement root, string localName) =>
        root.Elements()
            .FirstOrDefault(element => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.Value;
}
