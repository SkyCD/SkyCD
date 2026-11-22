using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.FileFormat.GCstar.Exceptions;
using SkyCD.Plugin.FileFormat.GCstar.Models;

namespace SkyCD.Plugin.FileFormat.GCstar.Parsing;

/// <summary>
/// Streaming parser for GCstar collection exports. Field values arrive as item
/// attributes (exact name first, case-insensitive duplicate ignored) while
/// description-like content arrives as child elements. Demo placeholders and
/// items missing required identity fields are skipped with counted reasons.
/// Parsing resources are bounded and DTD/entities are disabled. Attribute and
/// element names outside the known vocabulary are retained verbatim in field
/// values and additionally recorded as namespaced extension metadata with
/// unsupported-field diagnostics.
/// </summary>
public static class GCstarParser
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

    public static GCstarParseResult Parse(Stream source, CancellationToken cancellationToken)
    {
        using var streamReader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        using var reader = XmlReader.Create(streamReader, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or NotSupportedException or ArgumentException)
        {
            throw new GCstarMalformedDocumentException(exception.Message, exception);
        }
    }

    private static GCstarParseResult ParseCore(XmlReader reader, CancellationToken cancellationToken)
    {
        string? collectionType = null;
        string? version = null;
        var information = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unsupportedFields = new List<string>();
        var unsupportedSeen = new HashSet<string>(StringComparer.Ordinal);
        var skippedReasons = new List<string>();
        var skippedItemsCount = 0;
        var itemsSeen = 0;
        var items = new List<GCstarItem?>();
        var keptCounter = 0;
        var rootSeen = false;
        var inItem = false;
        var inInformation = false;
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<PendingField>();
        string? rawItemType = null;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    if (reader.Depth > MaxDepth)
                    {
                        throw new GCstarMalformedDocumentException(
                            $"GCstar collection nesting exceeds the maximum supported depth of {MaxDepth}.");
                    }

                    if (!rootSeen)
                    {
                        rootSeen = true;
                        if (!reader.LocalName.Equals(GCstarElementNames.RootElement, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new GCstarUnsupportedSchemaException(
                                $"expected {GCstarElementNames.RootElement} root, found '{reader.LocalName}'.");
                        }

                        collectionType = ReadAttribute(reader, GCstarElementNames.TypeAttribute);
                        version = ReadAttribute(reader, GCstarElementNames.VersionAttribute);
                        CollectAttributes(
                            reader,
                            [GCstarElementNames.TypeAttribute, GCstarElementNames.VersionAttribute, GCstarElementNames.ItemsAttribute],
                            information,
                            extra: null,
                            unsupportedFields,
                            unsupportedSeen,
                            owner: "collection",
                            lowercaseKeys: true);
                    }
                    else if (reader.LocalName.Equals(GCstarElementNames.ItemElement, StringComparison.OrdinalIgnoreCase))
                    {
                        if (inItem)
                        {
                            throw new GCstarMalformedDocumentException("Nested ITEM elements are not supported.");
                        }

                        if (itemsSeen >= MaxItems)
                        {
                            throw new GCstarMalformedDocumentException(
                                $"GCstar collection exceeds the maximum supported item count of {MaxItems}.");
                        }

                        inItem = true;
                        pending.Clear();
                        fields = new Dictionary<string, string>(StringComparer.Ordinal);
                        extra = new Dictionary<string, string>(StringComparer.Ordinal);
                        rawItemType = ReadAttribute(reader, GCstarElementNames.TypeAttribute);
                        CollectAttributes(
                            reader,
                            [GCstarElementNames.TypeAttribute],
                            fields,
                            extra,
                            unsupportedFields,
                            unsupportedSeen,
                            owner: "item");
                        if (reader.IsEmptyElement)
                        {
                            itemsSeen++;
                            EmitItem(items, ref keptCounter, ref skippedItemsCount, skippedReasons,
                                fields, extra, rawItemType, unsupportedFields, unsupportedSeen);
                            inItem = false;
                        }
                    }
                    else if (reader.LocalName.Equals(GCstarElementNames.InformationElement, StringComparison.OrdinalIgnoreCase)
                        && !inItem)
                    {
                        CollectInformationAttributes(reader, information, unsupportedFields, unsupportedSeen);
                        if (!reader.IsEmptyElement)
                        {
                            inInformation = true;
                        }
                    }
                    else if (inItem)
                    {
                        pending.Push(new PendingField(reader.LocalName, new StringBuilder()));
                    }
                    else if (inInformation)
                    {
                        pending.Push(new PendingField("information." + reader.LocalName, new StringBuilder()));
                    }
                    else
                    {
                        RecordUnsupported(reader.LocalName, unsupportedFields, unsupportedSeen);
                    }

                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (reader.HasValue && pending.Count > 0)
                    {
                        pending.Peek().Text.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (!rootSeen)
                    {
                        break;
                    }

                    if (reader.LocalName.Equals(GCstarElementNames.InformationElement, StringComparison.OrdinalIgnoreCase)
                        && !inItem)
                    {
                        while (pending.Count > 0)
                        {
                            FinishPendingField(pending.Pop(), information, fields, extra, unsupportedFields, unsupportedSeen);
                        }

                        inInformation = false;
                    }
                    else if (reader.LocalName.Equals(GCstarElementNames.ItemElement, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!inItem)
                        {
                            break;
                        }

                        while (pending.Count > 0)
                        {
                            FinishPendingField(pending.Pop(), information, fields, extra, unsupportedFields, unsupportedSeen);
                        }

                        itemsSeen++;
                        EmitItem(items, ref keptCounter, ref skippedItemsCount, skippedReasons,
                            fields, extra, rawItemType, unsupportedFields, unsupportedSeen);
                        inItem = false;
                    }
                    else if (pending.Count > 0)
                    {
                        FinishPendingField(pending.Pop(), information, fields, extra, unsupportedFields, unsupportedSeen);
                    }

                    break;
            }
        }

        if (!rootSeen)
        {
            throw new GCstarMalformedDocumentException("XML document has no collection root element.");
        }

        if (items.TrueForAll(static item => item is null))
        {
            throw new GCstarEmptyReportException();
        }

        var collectionName = information.TryGetValue("name", out var name) && !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : null;
        return new GCstarParseResult(
            collectionType,
            version,
            collectionName,
            information,
            items,
            unsupportedFields,
            skippedItemsCount,
            skippedReasons);
    }

    private static string? ReadAttribute(XmlReader reader, string name)
    {
        if (!reader.HasAttributes)
        {
            return null;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            if (reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase)
                && !reader.LocalName.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
            {
                var value = reader.Value;
                reader.MoveToElement();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        reader.MoveToElement();
        return null;
    }

    private static void CollectAttributes(
        XmlReader reader,
        string[] known,
        Dictionary<string, string> target,
        Dictionary<string, string>? extra,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen,
        string owner,
        bool lowercaseKeys = false)
    {
        if (!reader.HasAttributes)
        {
            return;
        }

        for (var i = 0; i < reader.AttributeCount; i++)
        {
            reader.MoveToAttribute(i);
            var name = reader.LocalName;
            if (name.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isKnown = false;
            foreach (var candidate in known)
            {
                if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    isKnown = true;
                    break;
                }
            }

            var storeKey = lowercaseKeys ? name.ToLowerInvariant() : name;
            if (!isKnown && !GCstarElementNames.IsKnownField(name))
            {
                var diagnostic = extra is not null ? $"GCstar.{name}" : owner + "@" + name;
                if (unsupportedSeen.Add(diagnostic))
                {
                    unsupportedFields.Add(diagnostic);
                }

                if (extra is not null)
                {
                    extra.TryAdd(diagnostic, reader.Value ?? string.Empty);
                }
            }

            var value = reader.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (FindFieldKey(target, storeKey) is null)
            {
                target[storeKey] = value;
            }
        }

        reader.MoveToElement();
    }

    private static void CollectInformationAttributes(
        XmlReader reader,
        Dictionary<string, string> information,
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
            if (name.StartsWith("xmlns", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!GCstarElementNames.IsKnownField(name) && unsupportedSeen.Add("information@" + name))
            {
                unsupportedFields.Add("information@" + name);
            }

            var value = (reader.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                information.TryAdd(name.ToLowerInvariant(), value);
            }
        }

        reader.MoveToElement();
    }

    private static string? FindFieldKey(Dictionary<string, string> fields, string name)
    {
        if (fields.ContainsKey(name))
        {
            return name;
        }

        foreach (var key in fields.Keys)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }

    private sealed record PendingField(string LocalName, StringBuilder Text);

    private static void FinishPendingField(
        PendingField pending,
        Dictionary<string, string> information,
        Dictionary<string, string> fields,
        Dictionary<string, string> extra,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        var text = pending.Text.ToString().Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (pending.LocalName.StartsWith("information.", StringComparison.Ordinal))
        {
            information.TryAdd(pending.LocalName["information.".Length..].ToLowerInvariant(), text);
            return;
        }

        var canonical = pending.LocalName.ToLowerInvariant();
        if (FindFieldKey(fields, canonical) is null)
        {
            fields[canonical] = text;
            if (!GCstarElementNames.IsKnownField(canonical))
            {
                extra.TryAdd($"GCstar.{pending.LocalName}", text);
                if (unsupportedSeen.Add($"GCstar.{pending.LocalName}"))
                {
                    unsupportedFields.Add($"GCstar.{pending.LocalName}");
                }
            }
        }
    }

    private static void EmitItem(
        List<GCstarItem?> items,
        ref int keptCounter,
        ref int skippedItemsCount,
        List<string> skippedReasons,
        Dictionary<string, string> fields,
        Dictionary<string, string> extra,
        string? rawItemType,
        List<string> unsupportedFields,
        HashSet<string> unsupportedSeen)
    {
        if (!string.IsNullOrWhiteSpace(rawItemType))
        {
            extra.TryAdd("GCstar.itemType", rawItemType.Trim());
            RecordUnsupported("item@type", unsupportedFields, unsupportedSeen);
        }

        if (GetField(fields, "id") is not { } id || string.IsNullOrWhiteSpace(id))
        {
            skippedItemsCount++;
            skippedReasons.Add("ITEM without an id was skipped.");
            return;
        }

        var trimmedId = id.Trim();
        var name = GetField(fields, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = GetField(fields, "title");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Unnamed {trimmedId}";
        }

        fields["__name"] = name.Trim();

        if (HasDemoMarker(fields, extra))
        {
            skippedItemsCount++;
            skippedReasons.Add($"ITEM '{trimmedId}' carries unregistered-version placeholder data and was skipped.");
            return;
        }

        var item = new GCstarItem(
            Index: keptCounter,
            Fields: new Dictionary<string, string>(fields, StringComparer.Ordinal),
            ExtraFields: new Dictionary<string, string>(extra, StringComparer.Ordinal));

        while (items.Count <= keptCounter)
        {
            items.Add(null);
        }

        items[keptCounter] = item;
        keptCounter++;
    }

    private static string? GetField(Dictionary<string, string> fields, string name)
    {
        var key = FindFieldKey(fields, name);
        return key is null ? null : fields[key];
    }

    private static bool HasDemoMarker(Dictionary<string, string> fields, Dictionary<string, string> extra)
    {
        foreach (var value in fields.Values)
        {
            if (GCstarElementNames.IsDemoValue(value))
            {
                return true;
            }
        }

        foreach (var value in extra.Values)
        {
            if (GCstarElementNames.IsDemoValue(value))
            {
                return true;
            }
        }

        return false;
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
