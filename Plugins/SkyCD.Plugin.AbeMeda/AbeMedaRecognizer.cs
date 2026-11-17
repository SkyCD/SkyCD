using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using SkyCD.Plugin.AbeMeda.Exceptions;

namespace SkyCD.Plugin.AbeMeda;

/// <summary>
/// Content-aware recognizer for abeMeda/NeoFinder-family XML exports. Recognition is
/// vocabulary-based with a bounded scan, so exports are not confused with other XML
/// formats sharing the .xml extension. Family-rooted documents score immediately;
/// anything else must accumulate clear vocabulary evidence.
/// </summary>
public static class AbeMedaRecognizer
{
    private const string SkyCdNamespaceUri = "urn:skycd:catalog";
    private const int ElementScanBudget = 512;
    private const int DepthScanLimit = 16;
    private const int AcceptScore = 5;
    private const int MaxFieldScore = 3;

    /// <summary>
    /// Inspects the stream content and returns true when it looks like an
    /// abeMeda/NeoFinder-family XML export. The stream position is preserved.
    /// </summary>
    public static bool IsAbeMedaXml(Stream stream)
    {
        try
        {
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            using var reader = XmlReader.Create(streamReader, AbeMedaParser.CreateSecureSettings());
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
        var countedFields = new HashSet<AbeMedaField>();
        var fieldScore = 0;
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
                    // Native SkyCD XML catalog; never an abeMeda export.
                    return false;
                }

                if (AbeMedaElementNames.IsFamilyRoot(rootName))
                {
                    // Family-rooted documents are always attempted; the parser
                    // reports empty or malformed content with dedicated errors.
                    return true;
                }
            }

            var elementName = reader.LocalName;
            if (countedElementNames.Add(elementName))
            {
                score += AbeMedaElementNames.ClassifyByName(elementName) switch
                {
                    AbeMedaElementKind.Volume => 2,
                    AbeMedaElementKind.Folder => 1,
                    AbeMedaElementKind.File => 1,
                    _ => 0
                };

                var elementField = AbeMedaElementNames.MapField(elementName);
                if (elementField != AbeMedaField.Unknown && countedFields.Add(elementField) && fieldScore < MaxFieldScore)
                {
                    score++;
                    fieldScore++;
                }
            }

            if (reader.HasAttributes)
            {
                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    var column = AbeMedaElementNames.MapField(reader.LocalName);
                    if (column != AbeMedaField.Unknown && countedFields.Add(column) && fieldScore < MaxFieldScore)
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
