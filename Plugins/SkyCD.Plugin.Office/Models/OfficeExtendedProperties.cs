using System.Linq;
using System.Xml.Linq;
using SkyCD.Plugin.Office.Parsing;

namespace SkyCD.Plugin.Office.Models;

/// <summary>Application-level metadata from <c>docProps/app.xml</c> inside an OOXML package.</summary>
internal sealed record OfficeExtendedProperties(
    string? Application,
    string? ApplicationVersion,
    string? Company,
    string? Manager,
    string? Template,
    long? PageCount,
    long? WordCount,
    long? CharacterCount,
    long? SlideCount)
{
    /// <summary>
    /// Reads the published scalar fields, matching direct children by local name regardless of namespace.
    /// Vector fields such as heading pairs and titles of parts are nested elements and are not published.
    /// </summary>
    public static OfficeExtendedProperties? Read(XDocument? document)
    {
        var root = document?.Root;
        if (root is null)
        {
            return null;
        }

        return new OfficeExtendedProperties(
            Text(root, "Application"),
            Text(root, "AppVersion"),
            Text(root, "Company"),
            Text(root, "Manager"),
            Text(root, "Template"),
            OfficeText.ParsePositiveInteger(Value(root, "Pages")),
            OfficeText.ParsePositiveInteger(Value(root, "Words")),
            OfficeText.ParsePositiveInteger(Value(root, "Characters")),
            OfficeText.ParsePositiveInteger(Value(root, "Slides")));
    }

    private static string? Text(XElement root, string localName) => OfficeText.Normalize(Value(root, localName));

    private static string? Value(XElement root, string localName) =>
        root.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;
}
