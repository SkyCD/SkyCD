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
/// .xml extension are never claimed. Where Is It? shares the <c>REPORT</c>/<c>ITEM</c>
/// shape but carries its own markers (<c>DISK_NAME</c>, <c>EXT</c>, <c>CRC</c>, …);
/// any such marker disqualifies the Disk Explorer claim. The scan is bounded.
/// </summary>
public static class DiskExplorerRecognizer
{
    private const int ElementScanBudget = 512;
    private const int DepthScanLimit = 16;

    private static readonly HashSet<string> WhereIsItMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "DISK_NAME", "DISK_TYPE", "DISK_NUM", "DISK_LOCATION",
        "EXT", "CRC", "CATEGORY", "FLAG"
    };

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

            if (WhereIsItMarkers.Contains(reader.LocalName))
            {
                return false;
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
            }

            if (reader.Depth >= DepthScanLimit || --remainingElements <= 0)
            {
                break;
            }
        }

        return rootName is not null && (hasTitle || hasTypedItem);
    }
}
