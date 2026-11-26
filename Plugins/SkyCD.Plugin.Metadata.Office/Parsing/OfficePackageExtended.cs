using System;
using System.IO;
using System.Linq;
using System.Xml;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads the application-level properties an OOXML package stores in <c>docProps/app.xml</c>.
/// </summary>
/// <remarks>
/// The part is taken from the library's own object model, so the element names are looked up on the parsed
/// tree instead of being walked as XML. Vector-valued entries such as <c>HeadingPairs</c> and
/// <c>TitlesOfParts</c> are never read: they describe the document's internal structure, not a fact the
/// catalog publishes, and the counts this plugin publishes come only from the scalar entries.
/// </remarks>
internal static class OfficePackageExtended
{
    public static void Apply(OfficeMetadata metadata, Func<ExtendedFilePropertiesPart?> read)
    {
        OpenXmlCompositeElement? properties;
        try
        {
            var part = read();
            properties = part?.Properties;
            if (properties is null)
            {
                return;
            }

            metadata.ApplicationName = OfficeText.Normalize(Text(properties, "Application"));
            metadata.ApplicationVersion = OfficeText.Normalize(Text(properties, "AppVersion"));
            metadata.Template = OfficeText.Normalize(Text(properties, "Template"));
            metadata.Company = OfficeText.Normalize(Text(properties, "Company"));
            metadata.Manager = OfficeText.Normalize(Text(properties, "Manager"));

            metadata.PageCount = OfficeText.ParsePositiveInteger(Text(properties, "Pages"));
            metadata.WordCount = OfficeText.ParsePositiveInteger(Text(properties, "Words"));
            metadata.CharacterCount = OfficeText.ParsePositiveInteger(Text(properties, "Characters"));
            metadata.SlideCount = OfficeText.ParsePositiveInteger(Text(properties, "Slides"));
        }
        catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException
                                              or XmlException)
        {
            // The application part is optional: a damaged one costs the extended fields only.
        }
    }

    private static string? Text(OpenXmlCompositeElement properties, string localName) =>
        properties.ChildElements
            .FirstOrDefault(child => child.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))
            ?.InnerText;
}
