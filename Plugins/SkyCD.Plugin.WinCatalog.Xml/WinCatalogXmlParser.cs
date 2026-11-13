using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.WinCatalog.Csv;
using SkyCD.Plugin.WinCatalog.Xml.Exceptions;

namespace SkyCD.Plugin.WinCatalog.Xml;

/// <summary>
/// Streaming parser for WinCatalog XML reports. Parsing resources are bounded and
/// DTD/external entity resolution is disabled. Element/attribute names are mapped
/// through <see cref="WinCatalogXmlProfile"/>; unmapped names are recorded as
/// unsupported-field diagnostics.
/// </summary>
public static class WinCatalogXmlParser
{
    public const int MaxDepth = 256;
    public const int MaxItems = 1_000_000;
    public const long MaxCharactersInDocument = 268_435_456;

    private const string IconFieldName = "Icon";

    /// <summary>
    /// Creates hardened XML reader settings: no DTD, no external entities, bounded document size.
    /// </summary>
    public static XmlReaderSettings CreateSecureSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharactersInDocument,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CheckCharacters = true,
            ConformanceLevel = ConformanceLevel.Document
        };
    }

    public static WinCatalogXmlParseResult Parse(Stream source, CancellationToken cancellationToken)
    {
        using var reader = XmlReader.Create(source, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or NotSupportedException or ArgumentException)
        {
            throw new WinCatalogXmlMalformedDocumentException(exception.Message, exception);
        }
    }

    private static WinCatalogXmlParseResult ParseCore(XmlReader reader, CancellationToken cancellationToken)
    {
        string? rootElement = null;
        string? schemaVersion = null;
        var unsupportedFields = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedItemsCount = 0;
        var skippedReasons = new List<string>();
        var items = new List<WinCatalogXmlItem?>();
        var frames = new Stack<ElementFrame>();
        var provisionalCounter = 0;
        var nodeCounter = 0;
        var isRootElement = false;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth > MaxDepth)
                    {
                        throw new WinCatalogXmlMalformedDocumentException(
                            $"WinCatalog XML report nesting exceeds the maximum supported depth of {MaxDepth}.");
                    }

                    if (items.Count >= MaxItems)
                    {
                        throw new WinCatalogXmlMalformedDocumentException(
                            $"WinCatalog XML report exceeds the maximum supported item count of {MaxItems}.");
                    }

                    if (rootElement is null)
                    {
                        rootElement = reader.LocalName;
                        isRootElement = true;
                        schemaVersion = DetectSchemaVersion(reader);
                    }

                    HandleElementStart(
                        reader,
                        frames,
                        items,
                        unsupportedFields,
                        unsupportedSeen,
                        skippedReasons,
                        ref skippedItemsCount,
                        ref provisionalCounter,
                        ref nodeCounter,
                        isRootElement);
                    isRootElement = false;
                    break;

                case XmlNodeType.EndElement:
                    HandleElementEnd(
                        reader,
                        frames,
                        items,
                        unsupportedFields,
                        unsupportedSeen,
                        skippedReasons,
                        ref skippedItemsCount,
                        ref nodeCounter);
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (frames.Count > 0 && reader.HasValue)
                    {
                        frames.Peek().AppendText(reader.Value);
                    }

                    break;
            }
        }

        if (items.All(item => item is null))
        {
            throw new WinCatalogXmlEmptyReportException();
        }

        return new WinCatalogXmlParseResult(
            rootElement,
            schemaVersion,
            items,
            unsupportedFields,
            skippedItemsCount,
            skippedReasons);
    }

    private static void HandleElementStart(
        XmlReader reader,
        Stack<ElementFrame> frames,
        List<WinCatalogXmlItem?> items,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen,
        List<string> skippedReasons,
        ref int skippedItemsCount,
        ref int provisionalCounter,
        ref int nodeCounter,
        bool isRootElement)
    {
        var localName = reader.LocalName;
        var fields = new Dictionary<WinCatalogCsvColumn, string>();
        string? iconValue = null;

        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                var attributeName = reader.LocalName;
                var attributeValue = reader.Value ?? string.Empty;
                if (attributeName.Equals(IconFieldName, StringComparison.OrdinalIgnoreCase))
                {
                    iconValue = attributeValue;
                    continue;
                }

                var column = WinCatalogXmlProfile.MapField(attributeName);
                if (column != WinCatalogCsvColumn.Unknown)
                {
                    fields.TryAdd(column, attributeValue);
                }
                else if (!isRootElement && unsupportedSeen.Add(attributeName))
                {
                    // Root-level attributes are report metadata (e.g. version, generator),
                    // not item fields; only entry-level unmapped names are diagnostics.
                    unsupportedFields.Add(attributeName);
                }
            }

            reader.MoveToElement();
        }

        var kind = WinCatalogXmlVocabulary.ClassifyElement(localName, fields);
        var parentItemFrame = FindParentItemFrame(frames);

        if (kind == WinCatalogXmlElementKind.Transparent)
        {
            if (parentItemFrame is not null)
            {
                var column = WinCatalogXmlProfile.MapField(localName);
                if (column != WinCatalogCsvColumn.Unknown)
                {
                    // Field leaf inside an entry element (e.g. <Comments>text</Comments>).
                    frames.Push(new ElementFrame(localName, column, parentItemFrame));
                    return;
                }

                RecordUnsupported(localName, unsupportedFields, unsupportedSeen);
            }

            // Wrapper element (e.g. <Disks>, <Results>) or unmapped complex element.
            frames.Push(new ElementFrame(localName));
            return;
        }

        // Entry-kind element (media / folder / file).
        if (reader.IsEmptyElement && fields.Count == 0)
        {
            // Empty element without any fields carries no information; ignore it.
            if (parentItemFrame is not null)
            {
                RecordUnsupported(localName, unsupportedFields, unsupportedSeen);
            }

            return;
        }

        if (reader.IsEmptyElement)
        {
            EmitItem(
                items,
                frames,
                skippedReasons,
                ref skippedItemsCount,
                ref nodeCounter,
                new ElementFrame(localName, kind, fields, iconValue, provisionalCounter++));
            return;
        }

        frames.Push(new ElementFrame(localName, kind, fields, iconValue, provisionalCounter++));
    }

    private static void HandleElementEnd(
        XmlReader reader,
        Stack<ElementFrame> frames,
        List<WinCatalogXmlItem?> items,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen,
        List<string> skippedReasons,
        ref int skippedItemsCount,
        ref int nodeCounter)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var frame = frames.Pop();
        if (frame.IsField)
        {
            var value = frame.TextValue();
            var parentItemFrame = FindParentItemFrame(frames);
            if (!string.IsNullOrWhiteSpace(value) && parentItemFrame is not null)
            {
                parentItemFrame.Fields.TryAdd(frame.FieldColumn!.Value, value);
            }

            return;
        }

        if (!frame.IsItem)
        {
            return;
        }

        var text = frame.TextValue();
        if (!string.IsNullOrWhiteSpace(text) && frame.Fields.Count == 0 && !frame.HasItemChildren)
        {
            // Text-only entry-kind element (e.g. <Volume>Disk 1</Volume>) is a field leaf.
            var parentItemFrame = FindParentItemFrame(frames);
            var column = WinCatalogXmlProfile.MapField(frame.LocalName);
            if (parentItemFrame is not null && column != WinCatalogCsvColumn.Unknown)
            {
                parentItemFrame.Fields.TryAdd(column, text);
            }

            return;
        }

        if (frame.Fields.Count == 0 && !frame.HasItemChildren)
        {
            skippedItemsCount++;
            skippedReasons.Add($"Element '{frame.LocalName}' has no recognized WinCatalog fields and was skipped.");
            return;
        }

        EmitItem(items, frames, skippedReasons, ref skippedItemsCount, ref nodeCounter, frame);
    }

    private static void EmitItem(
        List<WinCatalogXmlItem?> items,
        Stack<ElementFrame> frames,
        List<string> skippedReasons,
        ref int skippedItemsCount,
        ref int nodeCounter,
        ElementFrame frame)
    {
        var parentItemFrame = FindParentItemFrame(frames);
        if (parentItemFrame is not null)
        {
            parentItemFrame.HasItemChildren = true;
        }

        var item = new WinCatalogXmlItem(
            Index: frame.ProvisionalIndex,
            Kind: frame.Kind!.Value,
            ParentIndex: parentItemFrame?.ProvisionalIndex ?? -1,
            HasItemChildren: frame.HasItemChildren,
            Fields: frame.Fields!,
            IconValue: frame.IconValue);

        while (items.Count <= frame.ProvisionalIndex)
        {
            items.Add(null);
        }

        items[frame.ProvisionalIndex] = item;
        nodeCounter++;
    }

    private static ElementFrame? FindParentItemFrame(Stack<ElementFrame> frames)
    {
        foreach (var frame in frames)
        {
            if (frame.IsItem)
            {
                return frame;
            }
        }

        return null;
    }

    private static void RecordUnsupported(
        string name,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        if (unsupportedSeen.Add(name))
        {
            unsupportedFields.Add(name);
        }
    }

    private static string? DetectSchemaVersion(XmlReader reader)
    {
        string? version = null;
        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                var attributeName = reader.LocalName;
                if (attributeName.Equals("version", StringComparison.OrdinalIgnoreCase) ||
                    attributeName.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase) ||
                    attributeName.Equals("appVersion", StringComparison.OrdinalIgnoreCase) ||
                    attributeName.Equals("generator", StringComparison.OrdinalIgnoreCase))
                {
                    version ??= reader.Value;
                }
            }

            reader.MoveToElement();
        }

        ValidateSchemaVersion(version);
        return version;
    }

    private static void ValidateSchemaVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        // WinCatalog application versions: e.g. "2026", "WinCatalog 2024 Professional (24.0)".
        var yearMatch = System.Text.RegularExpressions.Regex.Match(version, @"(19|20)\d{2}");
        if (yearMatch.Success && int.Parse(yearMatch.Value, System.Globalization.CultureInfo.InvariantCulture) is >= 2019 and <= 2035)
        {
            return;
        }

        // Generic schema versions: e.g. "1.0", "2.1".
        if (System.Text.RegularExpressions.Regex.IsMatch(version, @"^\s*v?\d+(\.\d+)+\s*$"))
        {
            return;
        }

        throw new WinCatalogXmlUnsupportedSchemaException(version);
    }

    private sealed class ElementFrame
    {
        private StringBuilder? text;

        public ElementFrame(string localName)
        {
            LocalName = localName;
        }

        public ElementFrame(string localName, WinCatalogCsvColumn fieldColumn, ElementFrame parentItemFrame)
        {
            LocalName = localName;
            IsField = true;
            FieldColumn = fieldColumn;
            text = new StringBuilder();
        }

        public ElementFrame(
            string localName,
            WinCatalogXmlElementKind kind,
            Dictionary<WinCatalogCsvColumn, string> fields,
            string? iconValue,
            int provisionalIndex)
        {
            LocalName = localName;
            IsItem = true;
            Kind = kind;
            Fields = fields;
            IconValue = iconValue;
            ProvisionalIndex = provisionalIndex;
        }

        public string LocalName { get; }

        public bool IsItem { get; }

        public bool IsField { get; }

        public WinCatalogCsvColumn? FieldColumn { get; }

        public WinCatalogXmlElementKind? Kind { get; }

        public Dictionary<WinCatalogCsvColumn, string> Fields { get; } = new();

        public string? IconValue { get; }

        public bool HasItemChildren { get; set; }

        public int ProvisionalIndex { get; }

        public void AppendText(string value)
        {
            if (IsField || (IsItem && Fields.Count == 0 && !HasItemChildren))
            {
                text ??= new StringBuilder();
                text.Append(value);
            }
        }
        public string TextValue()
        {
            return text is null ? string.Empty : text.ToString().Trim();
        }
    }
}
