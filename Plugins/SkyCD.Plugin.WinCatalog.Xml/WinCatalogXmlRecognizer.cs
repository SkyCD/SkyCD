using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using SkyCD.Plugin.WinCatalog.Csv;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Content-aware recognizer for WinCatalog XML reports. Recognition is vocabulary-based
/// so WinCatalog exports are not confused with other XML formats sharing the .xml
/// extension (e.g. the SkyCD native XML catalog). The scan is bounded.
/// </summary>
public static class WinCatalogXmlRecognizer
{
    private const string SkyCdNamespaceUri = "urn:skycd:catalog";
    private const string WinCatalogTokenMarker = "{@";
    private const int ElementScanBudget = 512;
    private const int DepthScanLimit = 16;
    private const int AcceptScore = 5;
    private const int MaxFieldScore = 3;

    /// <summary>
    /// Inspects the stream content and returns true when it looks like a WinCatalog XML report.
    /// The stream position is preserved; the stream must be seekable.
    /// </summary>
    public static bool IsWinCatalogXml(Stream stream)
    {
        try
        {
            using var reader = XmlReader.Create(stream, WinCatalogXmlParser.CreateSecureSettings());
            return Scan(reader);
        }
        catch
        {
            return false;
        }
    }

    private static bool Scan(XmlReader reader)
    {
        string? rootName = null;
        var score = 0;
        var countedElementNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var countedFieldColumns = new HashSet<WinCatalogCsvColumn>();
        var fieldScore = 0;
        var tokenScored = false;
        var pathScored = false;
        var remainingElements = ElementScanBudget;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                continue;
            }

            if (rootName is null)
            {
                rootName = reader.LocalName;
                if (rootName.Equals("catalog", StringComparison.OrdinalIgnoreCase) &&
                    reader.NamespaceURI.Equals(SkyCdNamespaceUri, StringComparison.Ordinal))
                {
                    // Native SkyCD XML catalog; never a WinCatalog report.
                    return false;
                }

                if (rootName.Contains("wincatalog", StringComparison.OrdinalIgnoreCase))
                {
                    score += 3;
                }
            }

            var elementName = reader.LocalName;
            if (countedElementNames.Add(elementName))
            {
                score += WinCatalogXmlVocabulary.ClassifyByName(elementName) switch
                {
                    WinCatalogXmlElementKind.Media => 2,
                    WinCatalogXmlElementKind.Folder => 1,
                    WinCatalogXmlElementKind.File => 1,
                    _ => 0
                };
            }

            if (reader.HasAttributes)
            {
                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    var attributeName = reader.LocalName;
                    var attributeValue = reader.Value ?? string.Empty;

                    if (!tokenScored && attributeValue.Contains(WinCatalogTokenMarker, StringComparison.Ordinal))
                    {
                        score += 3;
                        tokenScored = true;
                    }

                    var column = WinCatalogXmlProfile.MapField(attributeName);
                    if (column == WinCatalogCsvColumn.Unknown || !countedFieldColumns.Add(column))
                    {
                        continue;
                    }

                    if ((column == WinCatalogCsvColumn.CatalogPath || column == WinCatalogCsvColumn.FilePath) && !pathScored)
                    {
                        score += 2;
                        pathScored = true;
                        continue;
                    }

                    if (fieldScore < MaxFieldScore)
                    {
                        score++;
                        fieldScore++;
                    }
                }

                reader.MoveToElement();
            }

            if (score >= AcceptScore)
            {
                return true;
            }

            if (reader.Depth >= DepthScanLimit || --remainingElements <= 0)
            {
                break;
            }
        }

        return score >= AcceptScore;
    }
}
