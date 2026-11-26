using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class ProjectXml
{
    public static XElement? Read(Stream stream, long limit, CancellationToken token, bool resolveNames = false)
    {
        using var bounded = new LimitedProjectStream(stream, limit);
        using var raw = new XmlTextReader(bounded)
        {
            // Resolve embeds database type qualifiers such as ListMgt::Sm2TiVideoClip in element names.
            Namespaces = !resolveNames,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(raw, new XmlReaderSettings
        {
            MaxCharactersInDocument = limit,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true
        });

        var parents = new Stack<XElement>();
        XElement? root = null;
        var elements = 0;
        string? resolveVersion = null;
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            if (reader.Depth > 96 || elements > 1_000_000)
                throw new ProjectLimitException("XML depth or element count");

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    elements++;
                    var element = new XElement(LocalName(reader));
                    while (reader.MoveToNextAttribute())
                    {
                        if (reader.Prefix != "xmlns" && reader.Name != "xmlns")
                            element.SetAttributeValue(LocalName(reader), reader.Value);
                    }
                    reader.MoveToElement();
                    if (parents.TryPeek(out var parent))
                        parent.Add(element);
                    else
                        root = element;
                    if (!reader.IsEmptyElement)
                        parents.Push(element);
                    break;
                case XmlNodeType.Comment when resolveNames && root is null:
                    const string versionKey = "DbAppVer=\"";
                    var comment = reader.Value.AsSpan();
                    if (comment.StartsWith(versionKey, StringComparison.Ordinal))
                    {
                        var version = comment[versionKey.Length..];
                        var end = version.IndexOf('"');
                        if (end is > 0 and <= 64)
                            resolveVersion = version[..end].ToString();
                    }
                    break;
                case XmlNodeType.EndElement:
                    parents.Pop();
                    break;
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                    parents.Peek().Add(reader.Value);
                    break;
            }
        }
        if (resolveVersion is not null)
            root?.AddAnnotation(resolveVersion);
        return root;
    }

    private static string LocalName(XmlReader reader)
    {
        var name = reader.LocalName;
        return XmlConvert.EncodeLocalName(name[(name.LastIndexOf(':') + 1)..]);
    }
}
