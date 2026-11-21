using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.WhereIsIt.Exceptions;
using SkyCD.Plugin.WhereIsIt.Models;

namespace SkyCD.Plugin.WhereIsIt.Parsing;

/// <summary>
/// Streaming parser for Where Is It? XML reports, following the Librer reference
/// mapping of the "List of all cataloged files and folders on selected disks"
/// export. Fields are first-wins per item; descriptions may span lines and carry
/// CDATA markers; unregistered-version <c>*** DEMO ***</c> placeholders and items
/// with missing required fields are skipped with reasons instead of partially
/// publishing. Parsing resources are bounded and DTD/external entities are disabled.
/// Unknown elements are retained as extension metadata.
/// </summary>
public static class WhereIsItParser
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

    public static WhereIsItParseResult Parse(Stream source, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        using var reader = XmlReader.Create(streamReader, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or NotSupportedException or ArgumentException)
        {
            throw new WhereIsItMalformedDocumentException(exception.Message, exception);
        }
    }

    private static WhereIsItParseResult ParseCore(XmlReader reader, CancellationToken cancellationToken)
    {
        string? title = null;
        var rootSeen = false;
        var unsupportedFields = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.Ordinal);
        var skippedReasons = new List<string>();
        var skippedItemsCount = 0;
        var items = new List<WhereIsItItem?>();
        var provisionalCounter = 0;
        var inItem = false;
        var pending = new Stack<PendingField>();
        Dictionary<WhereIsItField, string>? fields = null;
        Dictionary<string, string>? extra = null;
        var descriptionBuilder = new StringBuilder();
        string? rawItemType = null;
        WhereIsItElementKind kind = WhereIsItElementKind.Unknown;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth > MaxDepth)
                    {
                        throw new WhereIsItMalformedDocumentException(
                            $"Where Is It? XML report nesting exceeds the maximum supported depth of {MaxDepth}.");
                    }

                    if (items.Count >= MaxItems)
                    {
                        throw new WhereIsItMalformedDocumentException(
                            $"Where Is It? XML report exceeds the maximum supported item count of {MaxItems}.");
                    }

                    if (!rootSeen)
                    {
                        rootSeen = true;
                        if (!reader.LocalName.Equals(WhereIsItElementNames.RootElement, StringComparison.Ordinal))
                        {
                            throw new WhereIsItUnsupportedSchemaException(
                                $"expected {WhereIsItElementNames.RootElement} root, found '{reader.LocalName}'.");
                        }

                        title = ReadTitle(reader);
                        CollectRootUnknowns(reader, unsupportedFields, unsupportedSeen);
                    }
                    else if (reader.LocalName.Equals(WhereIsItElementNames.ItemElement, StringComparison.Ordinal))
                    {
                        if (inItem)
                        {
                            throw new WhereIsItMalformedDocumentException("Nested ITEM elements are not supported.");
                        }

                        rawItemType = ReadItemType(reader);
                        kind = WhereIsItElementNames.ClassifyItemType(rawItemType);
                        if (kind == WhereIsItElementKind.Disk)
                        {
                            throw new WhereIsItMalformedDocumentException(
                                $"incompatible kind of report: disk-level ITEM entries are not importable (ItemType=\"{rawItemType}\").");
                        }

                        inItem = true;
                        pending.Clear();
                        fields = new Dictionary<WhereIsItField, string>();
                        extra = new Dictionary<string, string>(StringComparer.Ordinal);
                        descriptionBuilder.Clear();
                        if (reader.IsEmptyElement)
                        {
                            EmitItem(items, ref provisionalCounter, ref skippedItemsCount, skippedReasons,
                                kind, rawItemType, fields, extra, descriptionBuilder.ToString(),
                                unsupportedFields, unsupportedSeen);
                            inItem = false;
                        }
                    }
                    else if (inItem)
                    {
                        var localName = reader.LocalName;
                        if (localName.Equals("DESCRIPTION", StringComparison.Ordinal))
                        {
                            pending.Push(new PendingField(true, localName, new StringBuilder()));
                        }
                        else
                        {
                            var column = WhereIsItElementNames.MapField(localName);
                            pending.Push(new PendingField(false, localName, new StringBuilder(), column));
                            if (column == WhereIsItField.Unknown)
                            {
                                RecordUnsupported(localName, unsupportedFields, unsupportedSeen);
                            }
                        }

                        if (reader.IsEmptyElement)
                        {
                            FinishPendingField(pending.Pop(), fields!, extra!, ref descriptionBuilder);
                        }
                    }
                    else
                    {
                        RecordUnsupported(reader.LocalName, unsupportedFields, unsupportedSeen);
                    }

                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (inItem && reader.HasValue && pending.Count > 0)
                    {
                        pending.Peek().Text.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (!rootSeen || !inItem)
                    {
                        break;
                    }

                    if (reader.LocalName.Equals(WhereIsItElementNames.ItemElement, StringComparison.Ordinal))
                    {
                        while (pending.Count > 0)
                        {
                            FinishPendingField(pending.Pop(), fields!, extra!, ref descriptionBuilder);
                        }

                        EmitItem(items, ref provisionalCounter, ref skippedItemsCount, skippedReasons,
                            kind, rawItemType, fields!, extra!, descriptionBuilder.ToString(),
                            unsupportedFields, unsupportedSeen);
                        inItem = false;
                    }
                    else if (pending.Count > 0
                        && (reader.LocalName.Equals(pending.Peek().LocalName, StringComparison.Ordinal)
                            || reader.LocalName.Equals("DESCRIPTION", StringComparison.Ordinal)))
                    {
                        FinishPendingField(pending.Pop(), fields!, extra!, ref descriptionBuilder);
                    }

                    break;
            }
        }

        if (!rootSeen)
        {
            throw new WhereIsItMalformedDocumentException("XML document has no report root element.");
        }

        if (items.TrueForAll(static item => item is null))
        {
            throw new WhereIsItEmptyReportException();
        }

        return new WhereIsItParseResult(
            title,
            items,
            unsupportedFields,
            skippedItemsCount,
            skippedReasons);
    }

    private sealed record PendingField(
        bool IsDescription,
        string LocalName,
        StringBuilder Text,
        WhereIsItField Column = WhereIsItField.Unknown);

    private static void FinishPendingField(
        PendingField pending,
        Dictionary<WhereIsItField, string> fields,
        Dictionary<string, string> extra,
        ref StringBuilder descriptionBuilder)
    {
        var text = pending.Text.ToString().Trim();
        if (pending.IsDescription)
        {
            descriptionBuilder.Append(text);
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (pending.Column != WhereIsItField.Unknown)
        {
            fields.TryAdd(pending.Column, text);
        }
        else
        {
            extra.TryAdd($"WhereIsIt.{pending.LocalName}", text);
        }
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
            if (reader.LocalName.Equals(WhereIsItElementNames.TitleAttribute, StringComparison.Ordinal))
            {
                var title = reader.Value;
                reader.MoveToElement();
                return string.IsNullOrWhiteSpace(title) ? null : title;
            }
        }

        reader.MoveToElement();
        return null;
    }

    private static string? ReadItemType(XmlReader reader)
    {
        if (!reader.HasAttributes)
        {
            return null;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (reader.LocalName.Equals(WhereIsItElementNames.ItemTypeAttribute, StringComparison.OrdinalIgnoreCase))
            {
                var value = reader.Value;
                reader.MoveToElement();
                return value;
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
            if (!name.Equals(WhereIsItElementNames.TitleAttribute, StringComparison.Ordinal)
                && !name.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase)
                && unsupportedSeen.Add("REPORT@" + name))
            {
                unsupportedFields.Add("REPORT@" + name);
            }
        }

        reader.MoveToElement();
    }

    private static void EmitItem(
        List<WhereIsItItem?> items,
        ref int provisionalCounter,
        ref int skippedItemsCount,
        List<string> skippedReasons,
        WhereIsItElementKind kind,
        string? rawItemType,
        Dictionary<WhereIsItField, string> fields,
        Dictionary<string, string> extra,
        string description,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        _ = unsupportedFields;
        _ = unsupportedSeen;
        var cleaned = StripCDataMarkers(description);
        if (!string.IsNullOrWhiteSpace(cleaned))
        {
            fields[WhereIsItField.Description] = cleaned;
        }

        if (kind is not (WhereIsItElementKind.File or WhereIsItElementKind.Folder))
        {
            skippedItemsCount++;
            skippedReasons.Add($"ITEM with ItemType '{rawItemType ?? "(missing)"}' is not importable and was skipped.");
            return;
        }

        if (!IsAcceptable(fields, out var reason))
        {
            skippedItemsCount++;
            skippedReasons.Add(reason);
            return;
        }

        var item = new WhereIsItItem(
            Index: provisionalCounter,
            Kind: kind,
            RawItemType: rawItemType,
            ParentIndex: -1,
            Fields: new Dictionary<WhereIsItField, string>(fields),
            ExtraFields: new Dictionary<string, string>(extra, StringComparer.Ordinal));

        while (items.Count <= provisionalCounter)
        {
            items.Add(null);
        }

        items[provisionalCounter] = item;
        provisionalCounter++;
    }

    private static bool IsAcceptable(Dictionary<WhereIsItField, string> fields, out string reason)
    {
        reason = string.Empty;
        foreach (var required in new[] { WhereIsItField.DiskName, WhereIsItField.Path, WhereIsItField.Name })
        {
            if (!fields.TryGetValue(required, out var value) || string.IsNullOrWhiteSpace(value))
            {
                reason = $"ITEM is missing required field '{required}' and was skipped.";
                return false;
            }

            if (WhereIsItElementNames.IsDemoValue(value))
            {
                reason = $"ITEM carries unregistered-version placeholder data and was skipped.";
                return false;
            }
        }

        if (fields.TryGetValue(WhereIsItField.Extension, out var extension)
            && !string.IsNullOrWhiteSpace(extension)
            && WhereIsItElementNames.IsDemoValue(extension))
        {
            reason = $"ITEM carries unregistered-version placeholder data and was skipped.";
            return false;
        }

        return true;
    }

    private static string StripCDataMarkers(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("<![CDATA[", StringComparison.Ordinal))
        {
            trimmed = trimmed["<![CDATA[".Length..];
        }

        if (trimmed.EndsWith("]]>", StringComparison.Ordinal))
        {
            trimmed = trimmed[..^"]]>".Length];
        }

        return trimmed.Trim();
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
}

