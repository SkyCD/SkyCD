using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Exceptions;
using SkyCD.Plugin.FileFormat.CDCatalogExpert.Models;

namespace SkyCD.Plugin.FileFormat.CDCatalogExpert.Parsing;

/// <summary>
/// Hardened Windows-1252 streaming parser for <c>zdc_index.xml</c> and disc
/// tree XML members.
/// </summary>
/// <remarks>
/// DTDs and external entities are prohibited, depth/element/attribute/text
/// limits are enforced before allocation, and cancellation is honored every
/// few hundred elements. Element and attribute names are case-sensitive.
/// Unknown attributes are retained; unknown elements produce a structured
/// warning instead of failing the import.
/// </remarks>
public static class ZdcCatalogXmlReader
{
    private static readonly Encoding Windows1252 = CreateWindows1252();

    static ZdcCatalogXmlReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Parses the catalog index member.</summary>
    public static ZdcCatalog ReadIndex(
        string memberName,
        byte[] payload,
        IList<string> warnings,
        CancellationToken cancellationToken = default)
    {
        var parser = new Parser(payload, warnings, cancellationToken);
        return parser.ParseCatalog();
    }

    /// <summary>Parses one disc-tree member.</summary>
    public static ZdcDiscTree ReadDiscTree(
        string memberName,
        byte[] payload,
        IList<string> warnings,
        CancellationToken cancellationToken = default)
    {
        var parser = new Parser(payload, warnings, cancellationToken);
        return parser.ParseDiscTree();
    }

    private static Encoding CreateWindows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static readonly string[] CatalogAttributes = ["N", "D"];
    private static readonly string[] GroupAttributes = ["N", "C"];
    private static readonly string[] DiscAttributes =
    [
        "N", "F", "P", "C", "M", "D", "V", "I", "T", "E", "K", "R", "O",
        "SO_A", "SO_B", "SO_C", "SO_D", "SO_E", "SO_F", "SO_G",
        "G", "W", "DS", "DI", "DB", "DR"
    ];
    private static readonly string[] TreeEntryAttributes = ["N", "A", "M", "C", "B", "D", "W"];
    private static readonly string[] FileEntryAttributes = ["N", "A", "M", "C", "B", "D", "W", "S"];

    private sealed class Parser(
        byte[] payload,
        IList<string> warnings,
        CancellationToken cancellationToken)
    {
        private readonly XmlReader reader = CreateReader(payload);
        private int elementCount;
        private int cancelCheckCounter;

        public ZdcCatalog ParseCatalog()
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (reader)
            {
                MoveToRoot("Z");
                return ReadCatalog();
            }
        }

        public ZdcDiscTree ParseDiscTree()
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (reader)
            {
                MoveToRoot("R");
                return ReadTreeRoot();
            }
        }

        private static XmlReader CreateReader(byte[] payload)
        {
            string text;
            try
            {
                var span = payload.AsSpan();
                if (span.Length >= 3 && span[0] == 0xEF && span[1] == 0xBB && span[2] == 0xBF)
                {
                    span = span[3..];
                }

                if (span.IndexOfAny((byte)0x81, (byte)0x8D, (byte)0x8F) >= 0 ||
                    span.IndexOf((byte)0x90) >= 0 ||
                    span.IndexOf((byte)0x9D) >= 0)
                {
                    throw new ZdcFormatException("XML member contains bytes that are not valid Windows-1252.");
                }

                text = Windows1252.GetString(span);
            }
            catch (DecoderFallbackException)
            {
                throw new ZdcFormatException("XML member contains bytes that are not valid Windows-1252.");
            }

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                CheckCharacters = true,
                MaxCharactersInDocument = ZdcFormat.MaxXmlDocumentCharacters,
                MaxCharactersFromEntities = 0,
                CloseInput = true
            };

            try
            {
                return XmlReader.Create(new StringReader(text), settings);
            }
            catch (XmlException exception)
            {
                throw new ZdcFormatException(exception.Message);
            }
        }

        private void MoveToRoot(string expected)
        {
            try
            {
                reader.MoveToContent();
            }
            catch (XmlException exception)
            {
                throw new ZdcFormatException(exception.Message);
            }

            if (reader.NodeType != XmlNodeType.Element ||
                !IsKnownElement(expected))
            {
                throw new ZdcFormatException($"expected root element '{expected}'.");
            }

            CountElement();
        }

        private bool IsKnownElement(string name) =>
            reader.LocalName == name && reader.NamespaceURI.Length == 0;

        private ZdcCatalog ReadCatalog()
        {
            var catalog = new ZdcCatalog();
            var values = ReadAttributes(CatalogAttributes, "Z", catalog.ExtensionAttributes);
            catalog.Name = RequireName(values, "Z");
            catalog.Description = values.GetValueOrDefault("D") ?? string.Empty;

            if (reader.IsEmptyElement)
            {
                return catalog;
            }

            while (AdvanceIntoScope())
            {
                if (!IsElement())
                {
                    continue;
                }

                if (IsKnownElement("G"))
                {
                    catalog.Children.Add(ReadGroup());
                }
                else if (IsKnownElement("L"))
                {
                    catalog.Children.Add(ReadDisc());
                }
                else
                {
                    SkipUnknown("Z");
                }
            }

            return catalog;
        }

        private ZdcGroup ReadGroup()
        {
            CountElement();
            var group = new ZdcGroup();
            var values = ReadAttributes(GroupAttributes, "G", group.ExtensionAttributes);
            group.Name = RequireName(values, "G");
            group.Timestamp = ValidateSigned(values.GetValueOrDefault("C"), "G.C", 64);

            if (reader.IsEmptyElement)
            {
                return group;
            }

            while (AdvanceIntoScope())
            {
                if (!IsElement())
                {
                    continue;
                }

                if (IsKnownElement("G"))
                {
                    group.Children.Add(ReadGroup());
                }
                else if (IsKnownElement("L"))
                {
                    group.Children.Add(ReadDisc());
                }
                else
                {
                    SkipUnknown("G");
                }
            }

            return group;
        }

        private ZdcDisc ReadDisc()
        {
            CountElement();
            var disc = new ZdcDisc();
            var values = ReadAttributes(DiscAttributes, "L", disc.ExtensionAttributes);
            disc.Name = RequireName(values, "L");
            disc.GuidText = ReadGuidReference(values, "L");
            disc.SourcePath = values.GetValueOrDefault("P");
            disc.Created = ValidateSigned(values.GetValueOrDefault("C"), "L.C", 64);
            disc.Modified = ValidateSigned(values.GetValueOrDefault("M"), "L.M", 64);
            disc.Description = values.GetValueOrDefault("D");
            disc.VolumeLabel = values.GetValueOrDefault("V");
            disc.FileSystem = values.GetValueOrDefault("I");
            disc.MediaType = ValidateSigned(values.GetValueOrDefault("T"), "L.T", 32);
            disc.VolumeSerial = ValidateSigned(values.GetValueOrDefault("E"), "L.E", 32);
            disc.Capacity = ValidateSigned(values.GetValueOrDefault("K"), "L.K", 64);
            disc.FreeSpace = ValidateSigned(values.GetValueOrDefault("R"), "L.R", 64);
            disc.Options = ValidateSigned(values.GetValueOrDefault("O"), "L.O", 32);
            disc.ScanFlagA = ValidateFlag(values.GetValueOrDefault("SO_A"), "L.SO_A");
            disc.ScanFlagB = ValidateFlag(values.GetValueOrDefault("SO_B"), "L.SO_B");
            disc.ScanFlagC = ValidateFlag(values.GetValueOrDefault("SO_C"), "L.SO_C");
            disc.ScanFlagD = ValidateFlag(values.GetValueOrDefault("SO_D"), "L.SO_D");
            disc.ScanFlagE = ValidateFlag(values.GetValueOrDefault("SO_E"), "L.SO_E");
            disc.ScanFlagF = ValidateFlag(values.GetValueOrDefault("SO_F"), "L.SO_F");
            disc.ScanFlagG = ValidateFlag(values.GetValueOrDefault("SO_G"), "L.SO_G");
            disc.CoverMemberName = values.GetValueOrDefault("G");
            disc.Keywords = values.GetValueOrDefault("W");
            disc.StorageLocation = values.GetValueOrDefault("DS");
            disc.IndexCode = values.GetValueOrDefault("DI");
            disc.Barcode = values.GetValueOrDefault("DB");
            disc.Rating = values.GetValueOrDefault("DR");

            if (reader.IsEmptyElement)
            {
                return disc;
            }

            while (AdvanceIntoScope())
            {
                if (IsElement())
                {
                    warnings.Add($"Unknown element '{Describe()}' inside 'L'; contents were skipped.");
                    SkipCurrentSubtree();
                }
            }

            return disc;
        }

        private ZdcDiscTree ReadTreeRoot()
        {
            var ignored = new List<KeyValuePair<string, string>>();
            ReadAttributes([], "R", ignored);
            foreach (var attribute in ignored)
            {
                warnings.Add($"Unknown attribute '{attribute.Key}' on 'R' was retained and skipped on save.");
            }

            var tree = new ZdcDiscTree();
            if (reader.IsEmptyElement)
            {
                return tree;
            }

            while (AdvanceIntoScope())
            {
                if (!IsElement())
                {
                    continue;
                }

                if (IsKnownElement("D"))
                {
                    tree.Entries.Add(ReadTreeEntry(isDirectory: true));
                }
                else if (IsKnownElement("F"))
                {
                    tree.Entries.Add(ReadTreeEntry(isDirectory: false));
                }
                else
                {
                    SkipUnknown("R");
                }
            }

            return tree;
        }

        private ZdcTreeEntry ReadTreeEntry(bool isDirectory)
        {
            CountElement();
            var elementName = isDirectory ? "D" : "F";
            var entry = new ZdcTreeEntry { IsDirectory = isDirectory };
            var values = ReadAttributes(
                isDirectory ? TreeEntryAttributes : FileEntryAttributes,
                elementName,
                entry.ExtensionAttributes);
            entry.Name = RequireName(values, elementName);
            entry.Attributes = ValidateSigned(values.GetValueOrDefault("A"), $"{elementName}.A", 32);
            entry.Modified = ValidateSigned(values.GetValueOrDefault("M"), $"{elementName}.M", 64);
            entry.Created = ValidateSigned(values.GetValueOrDefault("C"), $"{elementName}.C", 64);
            entry.TimestampB = ValidateSigned(values.GetValueOrDefault("B"), $"{elementName}.B", 64);
            entry.Description = values.GetValueOrDefault("D");
            entry.Keywords = values.GetValueOrDefault("W");
            if (!isDirectory)
            {
                if (!values.TryGetValue("S", out var size) || string.IsNullOrWhiteSpace(size))
                {
                    throw new ZdcFormatException($"file entry '{entry.Name}' is missing required attribute 'S'.");
                }

                if (!ulong.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    throw new ZdcFormatException($"file entry '{entry.Name}' has an invalid unsigned size '{size}'.");
                }

                entry.Size = size;
            }

            if (reader.IsEmptyElement)
            {
                return entry;
            }

            if (!isDirectory)
            {
                while (AdvanceIntoScope())
                {
                    if (IsElement())
                    {
                        warnings.Add($"Unknown element '{Describe()}' inside 'F'; contents were skipped.");
                        SkipCurrentSubtree();
                    }
                }

                return entry;
            }

            while (AdvanceIntoScope())
            {
                if (!IsElement())
                {
                    continue;
                }

                if (IsKnownElement("D"))
                {
                    entry.Children.Add(ReadTreeEntry(isDirectory: true));
                }
                else if (IsKnownElement("F"))
                {
                    entry.Children.Add(ReadTreeEntry(isDirectory: false));
                }
                else
                {
                    SkipUnknown("D");
                }
            }

            return entry;
        }

        /// <summary>
        /// Moves to the next node inside the current element scope. Returns
        /// <see langword="false"/> after the scope's end element is reached;
        /// the reader then remains positioned on that end element.
        /// </summary>
        private bool AdvanceIntoScope()
        {
            try
            {
                if (!reader.Read())
                {
                    throw new ZdcFormatException("XML member ended before its root element was closed.");
                }
            }
            catch (XmlException exception)
            {
                throw new ZdcFormatException(exception.Message);
            }

            if (reader.NodeType == XmlNodeType.EndElement)
            {
                return false;
            }

            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.Depth > ZdcFormat.MaxXmlDepth)
                {
                    throw new ZdcFormatException($"XML nesting exceeds the {ZdcFormat.MaxXmlDepth}-level limit.");
                }

                CountElement();
            }
            else if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                if (reader.Value.Length > ZdcFormat.MaxXmlTextLength)
                {
                    throw new ZdcFormatException("XML text value exceeds the safety limit.");
                }

                if (!string.IsNullOrWhiteSpace(reader.Value))
                {
                    warnings.Add($"Unexpected text content in '{Describe()}' was ignored.");
                }
            }

            return true;
        }

        private bool IsElement() => reader.NodeType == XmlNodeType.Element;

        private Dictionary<string, string> ReadAttributes(
            string[] knownNames,
            string elementName,
            List<KeyValuePair<string, string>> extensions)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!reader.HasAttributes)
            {
                return values;
            }

            var count = 0;
            for (var moved = reader.MoveToFirstAttribute(); moved; moved = reader.MoveToNextAttribute())
            {
                count++;
                if (count > ZdcFormat.MaxAttributesPerElement)
                {
                    throw new ZdcFormatException($"element '{elementName}' exceeds the attribute limit.");
                }

                var name = reader.Name;
                var value = reader.Value;
                if (value.Length > ZdcFormat.MaxXmlTextLength)
                {
                    throw new ZdcFormatException($"attribute '{name}' on '{elementName}' exceeds the text limit.");
                }

                var isKnown = reader.NamespaceURI.Length == 0 && Array.IndexOf(knownNames, name) >= 0;
                if (isKnown)
                {
                    if (!values.TryAdd(name, value))
                    {
                        throw new ZdcFormatException($"element '{elementName}' has a duplicate attribute '{name}'.");
                    }
                }
                else
                {
                    extensions.Add(new KeyValuePair<string, string>(name, value));
                }
            }

            reader.MoveToElement();
            return values;
        }

        private string RequireName(Dictionary<string, string> values, string elementName)
        {
            if (!values.TryGetValue("N", out var name) || string.IsNullOrWhiteSpace(name))
            {
                throw new ZdcFormatException($"element '{elementName}' is missing required attribute 'N'.");
            }

            return name;
        }

        private string ReadGuidReference(Dictionary<string, string> values, string elementName)
        {
            if (!values.TryGetValue("F", out var guid) || string.IsNullOrWhiteSpace(guid))
            {
                throw new ZdcFormatException($"element '{elementName}' is missing required disc reference 'F'.");
            }

            var trimmed = guid.Trim();
            var candidate = trimmed.StartsWith('{') && trimmed.EndsWith('}')
                ? trimmed[1..^1]
                : trimmed;
            if (!Guid.TryParse(candidate, out _))
            {
                throw new ZdcFormatException($"disc reference '{guid}' is not a valid GUID.");
            }

            return guid;
        }

        private string? ValidateSigned(string? raw, string field, int bits)
        {
            if (raw is null)
            {
                return null;
            }

            var valid = bits == 32
                ? int.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
                : long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
            if (!valid)
            {
                throw new ZdcFormatException($"attribute '{field}' has an invalid signed decimal value '{raw}'.");
            }

            return raw;
        }

        private string? ValidateFlag(string? raw, string field)
        {
            if (raw is null)
            {
                return null;
            }

            if (raw is not ("0" or "1"))
            {
                throw new ZdcFormatException($"attribute '{field}' must be '0' or '1'.");
            }

            return raw;
        }

        private void SkipUnknown(string parentElement)
        {
            warnings.Add($"Unknown element '{Describe()}' inside '{parentElement}'; contents were skipped.");
            SkipCurrentSubtree();
        }

        private void SkipCurrentSubtree()
        {
            if (reader.IsEmptyElement)
            {
                return;
            }

            var depth = reader.Depth;
            try
            {
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth)
                    {
                        return;
                    }
                }
            }
            catch (XmlException exception)
            {
                throw new ZdcFormatException(exception.Message);
            }

            throw new ZdcFormatException("XML member ended before its root element was closed.");
        }

        private string Describe()
        {
            var name = reader.Name;
            return name.Length == 0 ? reader.NodeType.ToString() : name;
        }

        private void CountElement()
        {
            elementCount++;
            if (elementCount > ZdcFormat.MaxXmlElements)
            {
                throw new ZdcFormatException($"XML member exceeds the {ZdcFormat.MaxXmlElements}-element limit.");
            }

            cancelCheckCounter++;
            if (cancelCheckCounter >= 256)
            {
                cancelCheckCounter = 0;
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
