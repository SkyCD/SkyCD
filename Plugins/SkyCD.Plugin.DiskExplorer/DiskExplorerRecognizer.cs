using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace SkyCD.Plugin.DiskExplorer;

/// <summary>
/// Content-aware recognizer for Disk Explorer Professional XML reports. Recognition
/// requires the exact <c>REPORT</c> root plus report shape (a title or an
/// <c>ITEM</c> element carrying <c>ItemType</c>), so unrelated XML files sharing the
/// .xml extension are never claimed. The scan is bounded.
/// </summary>
public static class DiskExplorerRecognizer
{
    private const int ElementScanBudget = 512;
    private const int DepthScanLimit = 16;

    public static bool IsDiskExplorerXml(Stream stream)
    {
        try
        {
            using var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
            using var reader = XmlReader.Create(streamReader, DiskExplorerParser.CreateSecureSettings());
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
        var hasTitle = false;
        var hasTypedItem = false;
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
                if (!rootName.Equals(DiskExplorerNames.RootElement, StringComparison.Ordinal))
                {
                    return false;
                }

                if (reader.HasAttributes)
                {
                    for (var i = 0; i < reader.AttributeCount; i++)
                    {
                        reader.MoveToAttribute(i);
                        if (reader.LocalName.Equals(DiskExplorerNames.TitleAttribute, StringComparison.Ordinal))
                        {
                            hasTitle = true;
                        }
                    }

                    reader.MoveToElement();
                }

                if (hasTitle)
                {
                    return true;
                }
            }
            else if (reader.LocalName.Equals(DiskExplorerNames.ItemElement, StringComparison.Ordinal)
                && reader.HasAttributes)
            {
                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    if (reader.LocalName.Equals(DiskExplorerNames.ItemTypeAttribute, StringComparison.OrdinalIgnoreCase))
                    {
                        hasTypedItem = true;
                    }
                }

                reader.MoveToElement();
                if (hasTypedItem)
                {
                    return true;
                }
            }

            if (reader.Depth >= DepthScanLimit || --remainingElements <= 0)
            {
                break;
            }
        }

        return false;
    }
}
