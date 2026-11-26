using System;
using System.IO;
using System.Text;
using System.Xml;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

/// <summary>
/// XML readers for untrusted project documents. A document type declaration and every external resource are
/// refused rather than resolved, so a project that references a DTD or entity is rejected instead of expanded.
/// </summary>
internal static class ProjectXml
{
    public const int MaxDepth = 96;

    public static XmlReader Create(Stream stream, long maxCharacters) =>
        XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CloseInput = false,
            MaxCharactersInDocument = maxCharacters
        });

    /// <summary>Reads the text of the element the reader is positioned on, truncated to <paramref name="maxCharacters"/>.</summary>
    public static string ReadElementText(XmlReader reader, int maxCharacters)
    {
        using var subtree = reader.ReadSubtree();
        var text = new StringBuilder();
        var buffer = new char[256];
        while (subtree.Read())
        {
            if (subtree.NodeType is not (XmlNodeType.Text or XmlNodeType.CDATA or
                XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace))
            {
                continue;
            }

            if (subtree.Depth != 1)
            {
                continue;
            }

            int read;
            while ((read = subtree.ReadValueChunk(buffer, 0, buffer.Length)) > 0)
            {
                var remaining = maxCharacters - text.Length;
                if (remaining <= 0)
                {
                    return text.ToString();
                }

                text.Append(buffer, 0, Math.Min(read, remaining));
                if (read > remaining)
                {
                    return text.ToString();
                }
            }
        }

        return text.ToString();
    }

    /// <summary>Finds the first direct child with the given local name and returns its text, or null.</summary>
    public static string? ReadFirstChildText(XmlReader reader, string childName, int maxCharacters)
    {
        using var subtree = reader.ReadSubtree();
        while (subtree.Read())
        {
            if (subtree.NodeType == XmlNodeType.Element && subtree.Depth == 1 &&
                subtree.LocalName == childName)
            {
                return ReadElementText(subtree, maxCharacters);
            }
        }

        return null;
    }
}
