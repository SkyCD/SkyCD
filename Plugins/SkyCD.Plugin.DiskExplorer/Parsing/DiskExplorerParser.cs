using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.DiskExplorer.Exceptions;
using SkyCD.Plugin.DiskExplorer.Models;

namespace SkyCD.Plugin.DiskExplorer.Parsing;

/// <summary>
/// Streaming parser for Disk Explorer Professional XML reports. Element and attribute
/// names are uppercase profile tokens; `ItemType` matches case-insensitively on import.
/// Parsing resources are bounded and DTD/external entity resolution is disabled.
/// Unknown elements and attributes are retained as extension metadata.
/// </summary>
public static class DiskExplorerParser
{
    public const int MaxDepth = 256;
    public const int MaxItems = 1_000_000;
    public const long MaxCharactersInDocument = 268_435_456;

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

    public static DiskExplorerParseResult Parse(Stream source, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        using var reader = XmlReader.Create(streamReader, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or NotSupportedException or ArgumentException)
        {
            throw new DiskExplorerMalformedDocumentException(exception.Message, exception);
        }
    }

    private static DiskExplorerParseResult ParseCore(XmlReader reader, CancellationToken cancellationToken)
    {
        string? title = null;
        var rootSeen = false;
        var unsupportedFields = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.Ordinal);
        var skippedReasons = new List<string>();
        var skippedItemsCount = 0;
        var items = new List<DiskExplorerItem?>();
        var frames = new Stack<ElementFrame>();
        var provisionalCounter = 0;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth > MaxDepth)
                    {
                        throw new DiskExplorerMalformedDocumentException(
                            $"Disk Explorer XML report nesting exceeds the maximum supported depth of {MaxDepth}.");
                    }

                    if (items.Count >= MaxItems)
                    {
                        throw new DiskExplorerMalformedDocumentException(
                            $"Disk Explorer XML report exceeds the maximum supported item count of {MaxItems}.");
                    }

                    if (!rootSeen)
                    {
                        rootSeen = true;
                        if (!reader.LocalName.Equals(DiskExplorerNames.RootElement, StringComparison.Ordinal))
                        {
                            throw new DiskExplorerUnsupportedSchemaException(
                                $"expected {DiskExplorerNames.RootElement} root, found '{reader.LocalName}'.");
                        }

                        title = ReadTitle(reader);
                        CollectRootUnknowns(reader, unsupportedFields, unsupportedSeen);
                    }
                    else
                    {
                        HandleElementStart(
                            reader,
                            frames,
                            items,
                            unsupportedFields,
                            unsupportedSeen,
                            skippedReasons,
                            ref skippedItemsCount,
                            ref provisionalCounter);
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (rootSeen && !reader.LocalName.Equals(DiskExplorerNames.RootElement, StringComparison.Ordinal))
                    {
                        HandleElementEnd(
                            reader,
                            frames,
                            items,
                            unsupportedFields,
                            unsupportedSeen,
                            skippedReasons,
                            ref skippedItemsCount);
                    }

                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (frames.Count > 0 && reader.HasValue)
                    {
                        frames.Peek().AppendText(reader.Value);
                    }

                    break;
            }
        }

        if (!rootSeen)
        {
            throw new DiskExplorerMalformedDocumentException("XML document has no report root element.");
        }

        if (items.TrueForAll(static item => item is null))
        {
            throw new DiskExplorerEmptyReportException();
        }

        return new DiskExplorerParseResult(
            title,
            items,
            unsupportedFields,
            skippedItemsCount,
            skippedReasons);
    }

    private static string? ReadTitle(XmlReader reader)
    {
        if (!reader.HasAttributes)
        {
            return null;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (reader.LocalName.Equals(DiskExplorerNames.TitleAttribute, StringComparison.Ordinal))
            {
                var title = reader.Value;
                reader.MoveToElement();
                return string.IsNullOrWhiteSpace(title) ? null : title;
            }
        }

        reader.MoveToElement();
        return null;
    }

    private static void CollectRootUnknowns(
        XmlReader reader,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        if (!reader.HasAttributes)
        {
            return;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            var name = reader.LocalName;
            if (!name.Equals(DiskExplorerNames.TitleAttribute, StringComparison.Ordinal)
                && !name.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)
                && unsupportedSeen.Add("REPORT@" + name))
            {
                unsupportedFields.Add("REPORT@" + name);
            }
        }

        reader.MoveToElement();
    }

    private static void HandleElementStart(
        XmlReader reader,
        Stack<ElementFrame> frames,
        List<DiskExplorerItem?> items,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen,
        List<string> skippedReasons,
        ref int skippedItemsCount,
        ref int provisionalCounter)
    {
        var localName = reader.LocalName;
        var parentItemFrame = FindParentItemFrame(frames);
        if (DiskExplorerNames.IsThumbnailName(localName))
        {
            // Thumbnail/cover payloads are presence markers only; payload bytes are never retained.
            if (parentItemFrame is not null)
            {
                parentItemFrame.HasThumbnail = true;
            }

            if (!reader.IsEmptyElement)
            {
                frames.Push(new ElementFrame(localName, new Dictionary<string, string>(StringComparer.Ordinal)));
            }

            return;
        }

        if (!localName.Equals(DiskExplorerNames.ItemElement, StringComparison.Ordinal))
        {
            // Unknown element: field leaf inside an entry, or a skipped wrapper.
            if (parentItemFrame is not null)
            {
                var unknownExtra = CollectUnknownAttributes(reader, localName, unsupportedFields, unsupportedSeen);
                frames.Push(new ElementFrame(localName, unknownExtra));
                if (reader.IsEmptyElement)
                {
                    frames.Pop();
                }

                return;
            }

            RecordUnsupported(localName, unsupportedFields, unsupportedSeen);
            return;
        }

        var fields = new Dictionary<DiskExplorerField, string>();
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        string? rawItemType = null;
        DiskExplorerElementKind kind = DiskExplorerElementKind.Unknown;

        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                var attributeName = reader.LocalName;
                if (attributeName.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (attributeName.Equals(DiskExplorerNames.ItemTypeAttribute, StringComparison.OrdinalIgnoreCase))
                {
                    rawItemType = reader.Value;
                    if (!DiskExplorerNames.TryParseItemType(rawItemType, out kind))
                    {
                        kind = DiskExplorerElementKind.Unknown;
                    }

                    continue;
                }

                var column = DiskExplorerNames.MapField(attributeName);
                if (column != DiskExplorerField.Unknown)
                {
                    fields.TryAdd(column, reader.Value ?? string.Empty);
                }
                else
                {
                    extra.TryAdd($"DiskExplorer.ITEM.{attributeName}", reader.Value ?? string.Empty);
                    RecordUnsupported("ITEM@" + attributeName, unsupportedFields, unsupportedSeen);
                }
            }

            reader.MoveToElement();
        }

        var frame = new ElementFrame(localName, kind, rawItemType, fields, extra, provisionalCounter++);
        if (reader.IsEmptyElement)
        {
            EmitItem(items, frames, frame);
            return;
        }

        frames.Push(frame);
    }

    private static Dictionary<string, string> CollectUnknownAttributes(
        XmlReader reader,
        string localName,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (reader.HasAttributes)
        {
            for (var i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);
                var attributeName = reader.LocalName;
                if (attributeName.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var column = DiskExplorerNames.MapField(attributeName);
                if (column != DiskExplorerField.Unknown)
                {
                    continue;
                }

                extra.TryAdd($"DiskExplorer.{localName}.{attributeName}", reader.Value ?? string.Empty);
                RecordUnsupported($"{localName}@{attributeName}", unsupportedFields, unsupportedSeen);
            }

            reader.MoveToElement();
        }

        return extra;
    }

    private static void HandleElementEnd(
        XmlReader reader,
        Stack<ElementFrame> frames,
        List<DiskExplorerItem?> items,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen,
        List<string> skippedReasons,
        ref int skippedItemsCount)
    {
        if (frames.Count == 0)
        {
            return;
        }

        var frame = frames.Pop();
        if (DiskExplorerNames.IsThumbnailName(frame.LocalName))
        {
            RecordUnsupported(frame.LocalName, unsupportedFields, unsupportedSeen);
            return;
        }

        if (frame.IsField)
        {
            var value = frame.TextValue();
            var parentItemFrame = FindParentItemFrame(frames);
            if (parentItemFrame is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                var column = DiskExplorerNames.MapField(frame.LocalName);
                if (column != DiskExplorerField.Unknown)
                {
                    parentItemFrame.Fields.TryAdd(column, value);
                }
                else
                {
                    parentItemFrame.Extra.TryAdd($"DiskExplorer.{frame.LocalName}", value);
                    RecordUnsupported(frame.LocalName, unsupportedFields, unsupportedSeen);
                }
            }

            foreach (var (key, extraValue) in frame.Extra)
            {
                parentItemFrame.Extra.TryAdd(key, extraValue);
            }

            return;
        }

        if (!frame.IsItem)
        {
            return;
        }

        var text = frame.TextValue();
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (frame.Fields.Count == 0 && frame.Extra.Count == 0 && !frame.HasItemChildren)
            {
                // Text-only ITEM carries the name.
                frame.Fields.TryAdd(DiskExplorerField.Name, text);
            }
            else
            {
                throw new DiskExplorerMalformedDocumentException(
                    $"ITEM element mixes child elements with text content.");
            }
        }

        if (frame.Kind == DiskExplorerElementKind.Unknown && !frame.HasItemChildren && frame.Fields.Count == 0 && frame.Extra.Count == 0)
        {
            skippedItemsCount++;
            skippedReasons.Add("Empty ITEM element carries no information and was skipped.");
            return;
        }

        EmitItem(items, frames, frame);
    }

    private static void EmitItem(
        List<DiskExplorerItem?> items,
        Stack<ElementFrame> frames,
        ElementFrame frame)
    {
        var parentItemFrame = FindParentItemFrame(frames);
        if (parentItemFrame is not null)
        {
            parentItemFrame.HasItemChildren = true;
        }

        var kind = frame.Kind;
        if (kind == DiskExplorerElementKind.Unknown)
        {
            kind = frame.HasItemChildren ? DiskExplorerElementKind.Folder : DiskExplorerElementKind.File;
        }

        ValidateNumbers(frame);
        var item = new DiskExplorerItem(
            Index: frame.ProvisionalIndex,
            Kind: kind,
            RawItemType: frame.RawItemType,
            ParentIndex: parentItemFrame?.ProvisionalIndex ?? -1,
            HasItemChildren: frame.HasItemChildren,
            HasThumbnail: frame.HasThumbnail,
            Fields: new Dictionary<DiskExplorerField, string>(frame.Fields),
            ExtraFields: new Dictionary<string, string>(frame.Extra, StringComparer.Ordinal));

        while (items.Count <= frame.ProvisionalIndex)
        {
            items.Add(null);
        }

        items[frame.ProvisionalIndex] = item;
    }

    private static void ValidateNumbers(ElementFrame frame)
    {
        foreach (var numeric in new[] { DiskExplorerField.Files, DiskExplorerField.Folders, DiskExplorerField.Size })
        {
            if (frame.Fields.TryGetValue(numeric, out var text) && !string.IsNullOrWhiteSpace(text)
                && !ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                throw new DiskExplorerMalformedDocumentException(
                    $"ITEM field '{numeric}' is not an unsigned decimal number: '{text}'.");
            }
        }
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

    private sealed class ElementFrame
    {
        private StringBuilder? text;

        public ElementFrame(string localName)
        {
            LocalName = localName;
        }

        public ElementFrame(string localName, Dictionary<string, string> extra)
        {
            LocalName = localName;
            IsField = true;
            foreach (var (key, value) in extra)
            {
                Extra[key] = value;
            }

            text = new StringBuilder();
        }

        public ElementFrame(
            string localName,
            DiskExplorerElementKind kind,
            string? rawItemType,
            Dictionary<DiskExplorerField, string> fields,
            Dictionary<string, string> extra,
            int provisionalIndex)
        {
            LocalName = localName;
            IsItem = true;
            Kind = kind;
            RawItemType = rawItemType;
            Fields = fields;
            Extra = extra;
            ProvisionalIndex = provisionalIndex;
        }

        public string LocalName { get; }

        public bool IsItem { get; }

        public bool IsField { get; }

        public DiskExplorerElementKind Kind { get; }

        public string? RawItemType { get; }

        public Dictionary<DiskExplorerField, string> Fields { get; } = new();

        public Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);

        public bool HasItemChildren { get; set; }

        public bool HasThumbnail { get; set; }

        public int ProvisionalIndex { get; }

        public void AppendText(string value)
        {
            if (!IsField && (!IsItem || Fields.Count > 0 || HasItemChildren))
            {
                return;
            }

            text ??= new StringBuilder();
            text.Append(value);
        }

        public string TextValue()
        {
            return text is null ? string.Empty : text.ToString().Trim();
        }
    }
}
