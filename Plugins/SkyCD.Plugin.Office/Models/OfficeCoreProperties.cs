using System;
using System.Linq;
using System.Xml.Linq;
using SkyCD.Plugin.Office.Parsing;

namespace SkyCD.Plugin.Office.Models;

/// <summary>Document-level metadata from <c>docProps/core.xml</c> inside an OOXML package.</summary>
internal sealed record OfficeCoreProperties(
    string? Title,
    string? Subject,
    string? Author,
    string? Keywords,
    string? Comments,
    string? Category,
    string? ContentStatus,
    string? LastSavedBy,
    string? RevisionNumber,
    string? Language,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? LastPrintedAt)
{
    /// <summary>Reads the published fields, matching direct children by local name regardless of prefix.</summary>
    public static OfficeCoreProperties? Read(XDocument? document)
    {
        var root = document?.Root;
        if (root is null)
        {
            return null;
        }

        return new OfficeCoreProperties(
            Text(root, "title"),
            Text(root, "subject"),
            Text(root, "creator"),
            Text(root, "keywords"),
            Text(root, "description"),
            Text(root, "category"),
            Text(root, "contentStatus"),
            Text(root, "lastModifiedBy"),
            Text(root, "revision"),
            Text(root, "language"),
            OfficeText.ParseTimestamp(Value(root, "created")),
            OfficeText.ParseTimestamp(Value(root, "modified")),
            OfficeText.ParseTimestamp(Value(root, "lastPrinted")));
    }

    private static string? Text(XElement root, string localName) => OfficeText.Normalize(Value(root, localName));

    private static string? Value(XElement root, string localName) =>
        root.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;
}
