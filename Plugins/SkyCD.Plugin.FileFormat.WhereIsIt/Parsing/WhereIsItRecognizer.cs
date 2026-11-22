using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using SkyCD.Plugin.FileFormat.WhereIsIt.Models;

namespace SkyCD.Plugin.FileFormat.WhereIsIt.Parsing;

/// <summary>
/// Content-aware recognizer for Where Is It? XML reports. Requires the exact
/// <c>REPORT</c> root plus at least one Where Is It? marker element, so unrelated
/// XML files sharing the .xml extension (including Disk Explorer reports with the
/// same root/item shape) are never claimed. The scan is bounded.
/// </summary>
public static class WhereIsItRecognizer
{
    private const int ElementScanBudget = 512;
    private const int DepthScanLimit = 16;

    public static bool IsWhereIsItXml(Stream stream)
    {
        try
        {
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            using var reader = XmlReader.Create(streamReader, WhereIsItParser.CreateSecureSettings());
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
                if (!rootName.Equals(WhereIsItElementNames.RootElement, StringComparison.Ordinal))
                {
                    return false;
                }
            }
            else if (WhereIsItElementNames.IsReportMarker(reader.LocalName))
            {
                return true;
            }

            if (reader.Depth >= DepthScanLimit || --remainingElements <= 0)
            {
                break;
            }
        }

        return false;
    }
}
