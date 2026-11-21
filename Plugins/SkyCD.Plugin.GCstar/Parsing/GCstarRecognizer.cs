using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using SkyCD.Plugin.GCstar.Models;

namespace SkyCD.Plugin.GCstar.Parsing;

/// <summary>
/// Content-aware recognizer for GCstar collection exports. Recognition is
/// vocabulary-based (root element plus collection attributes and item markers)
/// so GCstar exports are not confused with other XML formats sharing the .xml
/// extension (e.g. the SkyCD native catalog). The scan is bounded.
/// </summary>
public static class GCstarRecognizer
{
    private const string SkyCdNamespaceUri = "urn:skycd:catalog";
    private const int ElementScanBudget = 256;
    private const int DepthScanLimit = 8;
    private const int AcceptScore = 5;
    private const int MaxMarkerScore = 2;
    private const int MaxItemScore = 2;

    /// <summary>
    /// Inspects the stream content and returns true when it looks like a GCstar
    /// collection export. The stream position is preserved; the stream must be
    /// seekable.
    /// </summary>
    public static bool IsGCstarCollection(Stream stream)
    {
        var position = stream.CanSeek ? stream.Position : 0L;
        try
        {
            using (var streamReader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true))
            using (var reader = XmlReader.Create(streamReader, GCstarParser.CreateSecureSettings()))
            {
                return Scan(reader);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (stream.CanSeek)
            {
                try
                {
                    stream.Position = position;
                }
                catch (IOException)
                {
                }
                catch (NotSupportedException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private static bool Scan(XmlReader reader)
    {
        string? rootName = null;
        var score = 0;
        var markerScore = 0;
        var itemScore = 0;
        var informationSeen = false;
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
                    // Native SkyCD XML catalog; never a GCstar export.
                    return false;
                }

                if (rootName.Equals(GCstarElementNames.RootElement, StringComparison.OrdinalIgnoreCase))
                {
                    score += 2;
                    if (reader.HasAttributes)
                    {
                        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            seen.Add(reader.LocalName);
                        }

                        reader.MoveToElement();
                        if (seen.Contains(GCstarElementNames.TypeAttribute))
                        {
                            score++;
                        }

                        if (seen.Contains(GCstarElementNames.VersionAttribute))
                        {
                            score++;
                        }

                        if (seen.Contains(GCstarElementNames.ItemsAttribute))
                        {
                            score++;
                        }
                    }
                }
                else
                {
                    return false;
                }
            }

            if (reader.LocalName.Equals(GCstarElementNames.ItemElement, StringComparison.OrdinalIgnoreCase)
                && itemScore < MaxItemScore)
            {
                score++;
                itemScore++;
            }
            else if (reader.LocalName.Equals(GCstarElementNames.InformationElement, StringComparison.OrdinalIgnoreCase))
            {
                informationSeen = true;
            }
            else if (markerScore < MaxMarkerScore && GCstarElementNames.IsCollectionMarker(reader.LocalName))
            {
                score++;
                markerScore++;
            }

            if (reader.HasAttributes && markerScore < MaxMarkerScore)
            {
                for (var i = 0; i < reader.AttributeCount; i++)
                {
                    reader.MoveToAttribute(i);
                    if (GCstarElementNames.IsCollectionMarker(reader.LocalName))
                    {
                        score++;
                        markerScore++;
                        if (markerScore >= MaxMarkerScore)
                        {
                            break;
                        }
                    }
                }

                reader.MoveToElement();
            }

            if (informationSeen && itemScore > 0 && score >= AcceptScore)
            {
                return true;
            }

            if (score >= AcceptScore + 2)
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
