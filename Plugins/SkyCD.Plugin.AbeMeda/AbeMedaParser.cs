using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.AbeMeda.Exceptions;

namespace SkyCD.Plugin.AbeMeda;

/// <summary>
/// Streaming parser for abeMeda/NeoFinder-family XML exports. Parsing resources are
/// bounded and DTD/external entity resolution is disabled. Element and attribute names
/// map through <see cref="AbeMedaElementNames"/>; unmapped entry-level names are
/// recorded as unsupported-field diagnostics instead of failing the import.
/// </summary>
public static class AbeMedaParser
{
    public const int MaxDepth = 256;
    public const int MaxItems = 1_000_000;
    public const long MaxCharactersInDocument = 268_435_456;

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

    public static AbeMedaParseResult Parse(Stream source, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        using var reader = XmlReader.Create(streamReader, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or NotSupportedException or ArgumentException)
        {
            throw new AbeMedaMalformedDocumentException(exception.Message, exception);
        }
    }

    private static AbeMedaParseResult ParseCore(XmlReader reader, CancellationToken cancellationToken)
    {
        string? rootElement = null;
        string? schemaVersion = null;
        var unsupportedFields = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedReasons = new List<string>();
        var skippedItemsCount = 0;
        var items = new List<AbeMedaItem?>();
        var frames = new Stack<ElementFrame>();
        var provisionalCounter = 0;
        var isRootElement = false;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth > MaxDepth)
                    {
                        throw new AbeMedaMalformedDocumentException(
                            $"abeMeda XML export nesting exceeds the maximum supported depth of {MaxDepth}.");
                    }

                    if (items.Count >= MaxItems)
                    {
                        throw new AbeMedaMalformedDocumentException(
                            $"abeMeda XML export exceeds the maximum supported item count of {MaxItems}.");
                    }

                    if (rootElement is null)
                    {
                        rootElement = reader.LocalName;
                        isRootElement = true;
                        schemaVersion = DetectSchemaVersion(reader);
                        ValidateRoot(rootElement);
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
                        ref skippedItemsCount);
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (frames.Count > 0 && reader.HasValue)
                    {
                        frames.Peek().AppendText(reader.Value);
                    }

                    break;
            }
        }

        if (items.TrueForAll(static item => item is null))
        {
            throw new AbeMedaEmptyReportException();
        }

        return new AbeMedaParseResult(
            rootElement,
            schemaVersion,
            items,
            unsupportedFields,
            skippedItemsCount,
            skippedReasons);
    }

    private static void ValidateRoot(string rootElement)
    {
        if (!AbeMedaElementNames.IsFamilyRoot(rootElement))
        {
            throw new AbeMedaUnsupportedSchemaException(
                $"root element '{rootElement}' is not an abeMeda/NeoFinder-family export root.");
        }
    }

    private static void HandleElementStart(
        XmlReader reader,
        Stack<ElementFrame> frames,
        List<AbeMedaItem?> items,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen,
        List<string> skippedReasons,
        ref int skippedItemsCount,
        ref int provisionalCounter,
        bool isRootElement)
    {
        var localName = reader.LocalName;
        var fields = new Dictionary<AbeMedaField, string>();
        var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hasThumbnail = false;

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

                CollectValue(localName, attributeName, reader.Value ?? string.Empty,
                    fields, extra, ref hasThumbnail, isRootElement,
                    unsupportedFields, unsupportedSeen);
            }

            reader.MoveToElement();
        }

        var kind = AbeMedaElementNames.ClassifyByName(localName);
        var parentItemFrame = FindParentItemFrame(frames);

        if (AbeMedaElementNames.IsThumbnailName(localName))
        {
            // Thumbnail/cover payloads are presence markers only: native-catalog
            // fidelity is not promised, so payload bytes are never retained.
            if (parentItemFrame is not null)
            {
                parentItemFrame.HasThumbnail = true;
            }

            if (!reader.IsEmptyElement)
            {
                frames.Push(new ElementFrame(localName));
            }

            return;
        }

        if (kind is AbeMedaElementKind.Field)
        {
            if (parentItemFrame is not null)
            {
                frames.Push(new ElementFrame(localName, AbeMedaElementNames.MapField(localName), parentItemFrame, extra.Count > 0 ? extra : null));
                return;
            }

            RecordUnsupported(localName, unsupportedFields, unsupportedSeen);
            return;
        }

        if (kind is AbeMedaElementKind.Unknown or AbeMedaElementKind.Transparent)
        {
            if (parentItemFrame is not null && !isRootElement)
            {
                // Field-like leaf inside an entry element whose tag has no catalog meaning.
                frames.Push(new ElementFrame(localName, parentItemFrame, extra.Count > 0 ? extra : null));
                return;
            }

            frames.Push(new ElementFrame(localName));
            return;
        }

        // Volume / folder / file entry element.
        if (reader.IsEmptyElement && fields.Count == 0 && extra.Count == 0 && !hasThumbnail)
        {
            if (parentItemFrame is not null)
            {
                RecordUnsupported(localName, unsupportedFields, unsupportedSeen);
            }

            return;
        }

        var frame = new ElementFrame(localName, kind, fields, extra, hasThumbnail, provisionalCounter++);
        if (reader.IsEmptyElement)
        {
            EmitItem(items, frames, frame);
            return;
        }

        frames.Push(frame);
    }

    private static void HandleElementEnd(
        XmlReader reader,
        Stack<ElementFrame> frames,
        List<AbeMedaItem?> items,
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
                if (frame.FieldColumn != AbeMedaField.Unknown)
                {
                    parentItemFrame.Fields.TryAdd(frame.FieldColumn!.Value, value);
                }
                else if (AbeMedaElementNames.IsThumbnailName(frame.LocalName))
                {
                    parentItemFrame.HasThumbnail = true;
                }
                else
                {
                    parentItemFrame.Extra.TryAdd(Qualify(frame.LocalName), value);
                    RecordUnsupported(frame.LocalName, unsupportedFields, unsupportedSeen);
                }
            }
            else if (frame.Extra.Count > 0)
            {
                foreach (var (key, extraValue) in frame.Extra)
                {
                    parentItemFrame.Extra.TryAdd(key, extraValue);
                }
            }

            return;
        }

        if (!frame.IsItem)
        {
            return;
        }

        var text = frame.TextValue();
        if (!string.IsNullOrWhiteSpace(text) && frame.Fields.Count == 0 && frame.Extra.Count == 0 && !frame.HasItemChildren)
        {
            // Text-only entry-kind element (e.g. <Volume>Backup</Volume>) carries the name.
            var parentItemFrame = FindParentItemFrame(frames);
            if (parentItemFrame is not null)
            {
                parentItemFrame.Fields.TryAdd(AbeMedaField.Name, text);
                RecordUnsupported(frame.LocalName, unsupportedFields, unsupportedSeen);
                return;
            }

            frame.Fields.TryAdd(AbeMedaField.Name, text);
        }

        if (frame.Fields.Count == 0 && frame.Extra.Count == 0 && !frame.HasThumbnail && !frame.HasItemChildren)
        {
            skippedItemsCount++;
            skippedReasons.Add($"Element '{frame.LocalName}' has no recognized abeMeda fields and was skipped.");
            return;
        }

        EmitItem(items, frames, frame);
    }

    private static void EmitItem(
        List<AbeMedaItem?> items,
        Stack<ElementFrame> frames,
        ElementFrame frame)
    {
        var parentItemFrame = FindParentItemFrame(frames);
        if (parentItemFrame is not null)
        {
            parentItemFrame.HasItemChildren = true;
        }

        var item = new AbeMedaItem(
            Index: frame.ProvisionalIndex,
            Kind: frame.Kind!.Value,
            ParentIndex: parentItemFrame?.ProvisionalIndex ?? -1,
            HasItemChildren: frame.HasItemChildren,
            HasThumbnail: frame.HasThumbnail,
            Fields: new Dictionary<AbeMedaField, string>(frame.Fields),
            ExtraFields: new Dictionary<string, string>(frame.Extra, StringComparer.OrdinalIgnoreCase));

        while (items.Count <= frame.ProvisionalIndex)
        {
            items.Add(null);
        }

        items[frame.ProvisionalIndex] = item;
    }

    private static void CollectValue(
        string elementName,
        string attributeName,
        string value,
        Dictionary<AbeMedaField, string> fields,
        Dictionary<string, string> extra,
        ref bool hasThumbnail,
        bool isRootElement,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        var column = AbeMedaElementNames.MapField(attributeName);
        if (column != AbeMedaField.Unknown)
        {
            fields.TryAdd(column, value);
            return;
        }

        if (AbeMedaElementNames.IsThumbnailName(attributeName))
        {
            hasThumbnail = true;
            return;
        }

        if (!isRootElement)
        {
            extra.TryAdd(Qualify(elementName, attributeName), value);
            RecordUnsupported(attributeName, unsupportedFields, unsupportedSeen);
        }
    }

    private static string Qualify(string localName) => $"AbeMeda.{localName}";

    private static string Qualify(string elementName, string attributeName) =>
        $"AbeMeda.{elementName}.{attributeName}";

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

        return string.IsNullOrWhiteSpace(version) ? null : version;
    }

    private sealed class ElementFrame
    {
        private StringBuilder? text;

        public ElementFrame(string localName)
        {
            LocalName = localName;
        }

        public ElementFrame(
            string localName,
            AbeMedaField fieldColumn,
            ElementFrame parentItemFrame,
            Dictionary<string, string>? extra)
        {
            LocalName = localName;
            IsField = true;
            FieldColumn = fieldColumn;
            if (extra is not null)
            {
                foreach (var (key, value) in extra)
                {
                    Extra[key] = value;
                }
            }

            text = new StringBuilder();
        }

        public ElementFrame(
            string localName,
            ElementFrame parentItemFrame,
            Dictionary<string, string>? extra)
        {
            LocalName = localName;
            IsField = true;
            FieldColumn = AbeMedaField.Unknown;
            if (extra is not null)
            {
                foreach (var (key, value) in extra)
                {
                    Extra[key] = value;
                }
            }

            text = new StringBuilder();
        }

        public ElementFrame(
            string localName,
            AbeMedaElementKind kind,
            Dictionary<AbeMedaField, string> fields,
            Dictionary<string, string> extra,
            bool hasThumbnail,
            int provisionalIndex)
        {
            LocalName = localName;
            IsItem = true;
            Kind = kind;
            Fields = fields;
            Extra = extra;
            HasThumbnail = hasThumbnail;
            ProvisionalIndex = provisionalIndex;
        }

        public string LocalName { get; }

        public bool IsItem { get; }

        public bool IsField { get; }

        public AbeMedaField? FieldColumn { get; }

        public AbeMedaElementKind? Kind { get; }

        public Dictionary<AbeMedaField, string> Fields { get; } = new();

        public Dictionary<string, string> Extra { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HasThumbnail { get; set; }

        public bool HasItemChildren { get; set; }

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

