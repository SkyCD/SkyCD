using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.Tellico.Exceptions;

namespace SkyCD.Plugin.Tellico;

/// <summary>
/// Streaming parser for Tellico collections: native <c>.tc</c> archives
/// (a ZIP holding <c>tellico.xml</c> plus an <c>images/</c> folder, with a
/// legacy <c>bookcase.xml</c> fallback) and plain Tellico XML exports.
/// Document shape follows the published Tellico DTD (v11; syntax version 12
/// adds filter-rule values only). The DTD itself is never fetched or used
/// for validation: external resolution is disabled and entity expansion is
/// bounded. Field values are typed from the document's own field
/// definitions; unknown elements are retained as extension metadata with
/// unsupported-field diagnostics.
/// </summary>
public static class TellicoParser
{
    public const int MaxDepth = 256;
    public const long MaxCharactersInDocument = 268_435_456;
    public const long MaxCharactersFromEntities = 10_485_760;
    public const int MaxZipEntries = 100_000;
    public const long MaxZipEntryBytes = 268_435_456;
    public const long MaxZipTotalBytes = 1_073_741_824;
    public const long MaxEntries = 1_000_000;
    public const int MaxFieldTextChars = 1_048_576;
    public const long MaxImageTextChars = 67_108_864;

    private const string DocumentEntryName = "tellico.xml";
    private const string LegacyDocumentEntryName = "bookcase.xml";
    private const string ImagesPrefix = "images/";

    private static readonly Regex CanonicalNumber = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$", RegexOptions.Compiled);

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyyMMdd",
        "dd-MM-yyyy", "dd/MM/yyyy", "dd.MM.yyyy"
    ];

    public static XmlReaderSettings CreateSecureSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
            MaxCharactersInDocument = MaxCharactersInDocument,
            MaxCharactersFromEntities = MaxCharactersFromEntities,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            CheckCharacters = true,
            ConformanceLevel = ConformanceLevel.Document
        };
    }

    public static TellicoParseResult Parse(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (IsZipArchive(source))
        {
            return ParseArchive(source, cancellationToken);
        }

        using var reader = XmlReader.Create(source, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or DecoderFallbackException)
        {
            throw new TellicoMalformedException(exception.Message);
        }
    }

    private static bool IsZipArchive(Stream source)
    {
        if (!source.CanSeek)
        {
            return false;
        }

        var position = source.Position;
        try
        {
            Span<byte> magic = stackalloc byte[4];
            var read = source.Read(magic);
            return read == 4 && magic[0] == 0x50 && magic[1] == 0x4B && magic[2] == 0x03 && magic[3] == 0x04;
        }
        finally
        {
            source.Position = position;
        }
    }

    private static TellicoParseResult ParseArchive(Stream source, CancellationToken cancellationToken)
    {
        if (!source.CanSeek)
        {
            throw new TellicoMalformedException("collection archive stream must be seekable.");
        }

        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
        long totalBytes = 0;
        var entryCount = 0;
        ZipArchiveEntry? documentEntry = null;
        ZipArchiveEntry? legacyEntry = null;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entryCount++;
            if (entryCount > MaxZipEntries)
            {
                throw new TellicoUnsafeArchiveException(
                    $"archive exceeds the maximum supported entry count of {MaxZipEntries}.");
            }

            var name = entry.FullName.Replace('\\', '/');
            if (IsUnsafeEntryName(name))
            {
                throw new TellicoUnsafeArchiveException($"archive entry has an unsafe path: '{entry.FullName}'.");
            }

            totalBytes += entry.Length;
            if (entry.Length > MaxZipEntryBytes || totalBytes > MaxZipTotalBytes)
            {
                throw new TellicoUnsafeArchiveException("archive exceeds the maximum supported unpacked size.");
            }

            if (name.Equals(DocumentEntryName, StringComparison.OrdinalIgnoreCase))
            {
                documentEntry ??= entry;
            }
            else if (name.Equals(LegacyDocumentEntryName, StringComparison.OrdinalIgnoreCase))
            {
                legacyEntry ??= entry;
            }
        }

        var document = documentEntry ?? legacyEntry;
        if (document is null)
        {
            throw new TellicoUnsupportedSchemaException(
                $"archive has no '{DocumentEntryName}' document entry.");
        }

        using var documentStream = new MemoryStream();
        using (var entryStream = document.Open())
        {
            CopyBounded(entryStream, documentStream, MaxZipEntryBytes, cancellationToken);
        }

        documentStream.Position = 0;
        using var reader = XmlReader.Create(documentStream, CreateSecureSettings());
        try
        {
            return ParseCore(reader, cancellationToken);
        }
        catch (Exception exception) when (exception is XmlException or IOException or DecoderFallbackException)
        {
            throw new TellicoMalformedException(exception.Message);
        }
    }

    private static bool IsUnsafeEntryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('/') || name.StartsWith('\\'))
        {
            return true;
        }

        if (name.Length > 1 && name[1] == ':')
        {
            return true;
        }

        foreach (var segment in name.Split('/'))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    private static void CopyBounded(Stream source, Stream target, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            if (total > maxBytes)
            {
                throw new TellicoUnsafeArchiveException("archive entry exceeds the maximum supported size.");
            }

            target.Write(buffer, 0, read);
        }
    }

    private static TellicoParseResult ParseCore(XmlReader reader, CancellationToken cancellationToken)
    {
        string? syntaxVersion = null;
        var collections = new List<TellicoCollection>();
        CollectionState? current = null;
        EntryState? entry = null;
        var frames = new Stack<FieldFrame>();
        long elementCount = 0;
        var rootSeen = false;

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    elementCount++;
                    if (elementCount > MaxEntries * 100)
                    {
                        throw new TellicoMalformedException("collection exceeds the maximum supported element count.");
                    }

                    if (reader.Depth > MaxDepth)
                    {
                        throw new TellicoMalformedException(
                            $"collection nesting exceeds the maximum supported depth of {MaxDepth}.");
                    }

                    if (!rootSeen)
                    {
                        rootSeen = true;
                        if (!reader.LocalName.Equals(TellicoElementNames.RootElement, StringComparison.OrdinalIgnoreCase))
                        {
                            throw new TellicoUnsupportedSchemaException(
                                $"expected {TellicoElementNames.RootElement} root, found '{reader.LocalName}'.");
                        }

                        syntaxVersion = ReadAttribute(reader, TellicoElementNames.SyntaxVersionAttribute);
                    }
                    else if (current is null && reader.LocalName.Equals(TellicoElementNames.CollectionElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current = new CollectionState(
                            title: ReadAttribute(reader, "title") ?? "Untitled collection",
                            type: ReadAttribute(reader, "type") ?? string.Empty,
                            entryTitle: ReadAttribute(reader, "entryTitle"));
                    }
                    else if (current is not null && entry is null
                        && reader.LocalName.Equals(TellicoElementNames.FieldsElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InFields = !reader.IsEmptyElement;
                    }
                    else if (current is not null && entry is null && current.InFields
                        && reader.LocalName.Equals(TellicoElementNames.FieldElement, StringComparison.OrdinalIgnoreCase))
                    {
                        if (current.PendingField is not null)
                        {
                            throw new TellicoMalformedException("Nested field definitions are not supported.");
                        }

                        current.PendingField = ReadFieldDefinition(reader);
                        current.PendingProps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        if (reader.IsEmptyElement)
                        {
                            current.CommitField();
                        }
                    }
                    else if (current is not null && entry is null && current.PendingField is not null
                        && reader.LocalName.Equals(TellicoElementNames.PropertyElement, StringComparison.OrdinalIgnoreCase))
                    {
                        var propName = ReadAttribute(reader, "name");
                        if (!string.IsNullOrWhiteSpace(propName) && !reader.IsEmptyElement)
                        {
                            current.PendingPropName = propName.Trim();
                            current.PendingPropText = new StringBuilder();
                        }
                    }
                    else if (current is not null && entry is null
                        && reader.LocalName.Equals(TellicoElementNames.BorrowersElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InBorrowers = !reader.IsEmptyElement;
                    }
                    else if (current is not null && entry is null
                        && reader.LocalName.Equals(TellicoElementNames.ImagesElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InImages = !reader.IsEmptyElement;
                    }
                    else if (current is not null
                        && reader.LocalName.Equals(TellicoElementNames.EntryElement, StringComparison.OrdinalIgnoreCase)
                        && entry is null && !current.InBorrowers && !current.InImages)
                    {
                        var created = current.EntryCount + 1;
                        if (created > MaxEntries)
                        {
                            throw new TellicoMalformedException(
                                $"collection exceeds the maximum supported entry count of {MaxEntries}.");
                        }

                        entry = new EntryState(ReadAttribute(reader, TellicoElementNames.IdElement));
                        frames.Clear();
                        if (reader.IsEmptyElement)
                        {
                            current.FinishEntry(entry);
                            entry = null;
                        }
                    }
                    else if (current is not null && entry is null && current.InBorrowers
                        && reader.LocalName.Equals(TellicoElementNames.BorrowerElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.PendingBorrowerName = new StringBuilder();
                        current.InLoan = false;
                    }
                    else if (current is not null && entry is null && current.PendingBorrowerName is not null
                        && reader.LocalName.Equals(TellicoElementNames.LoanElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.PendingLoan = new TellicoLoan(
                            Borrower: string.Empty,
                            EntryRef: ReadAttribute(reader, "entryRef") ?? string.Empty,
                            LoanDate: ReadAttribute(reader, "loanDate"),
                            DueDate: ReadAttribute(reader, "dueDate"),
                            Uid: ReadAttribute(reader, "uid"));
                        current.InLoan = true;
                        if (reader.IsEmptyElement)
                        {
                            current.InLoan = false;
                        }
                    }
                    else if (current is not null && entry is null && current.InImages
                        && reader.LocalName.Equals(TellicoElementNames.ImageElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.PendingImage = new PendingImage(
                            ReadAttribute(reader, TellicoElementNames.IdElement),
                            ReadAttribute(reader, "format"),
                            ReadAttribute(reader, "width"),
                            ReadAttribute(reader, "height"),
                            new StringBuilder());
                        if (reader.IsEmptyElement)
                        {
                            current.CommitImage();
                        }
                    }
                    else if (current is not null && entry is not null)
                    {
                        var frame = new FieldFrame(
                            reader.LocalName,
                            ReadAttribute(reader, TellicoElementNames.IdElement));
                        if (reader.IsEmptyElement)
                        {
                            current.FinishField(entry, frame);
                        }
                        else
                        {
                            frames.Push(frame);
                        }
                    }

                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace:
                    if (current?.PendingPropText is not null && current.PendingPropName is not null)
                    {
                        AppendBounded(current.PendingPropText, reader.Value, MaxFieldTextChars, "property");
                    }
                    else if (current?.PendingBorrowerName is not null && !current.InLoan)
                    {
                        AppendBounded(current.PendingBorrowerName, reader.Value, MaxFieldTextChars, "borrower");
                    }
                    else if (current?.PendingImage is not null && frames.Count == 0 && entry is null)
                    {
                        AppendBounded(current.PendingImage.Text, reader.Value, MaxImageTextChars, "image");
                    }
                    else if (frames.Count > 0)
                    {
                        AppendBounded(frames.Peek().Text, reader.Value, MaxFieldTextChars, "field");
                    }

                    break;

                case XmlNodeType.EndElement:
                    if (!rootSeen)
                    {
                        break;
                    }

                    if (reader.LocalName.Equals(TellicoElementNames.CollectionElement, StringComparison.OrdinalIgnoreCase))
                    {
                        if (current is not null)
                        {
                            collections.Add(current.Complete(syntaxVersion));
                            current = null;
                        }
                    }
                    else if (current is not null && entry is null
                        && reader.LocalName.Equals(TellicoElementNames.FieldsElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InFields = false;
                    }
                    else if (current is not null && entry is null && current.PendingField is not null
                        && reader.LocalName.Equals(TellicoElementNames.FieldElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.CommitField();
                    }
                    else if (current is not null && entry is null && current.PendingPropName is not null
                        && reader.LocalName.Equals(TellicoElementNames.PropertyElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.CommitProp();
                    }
                    else if (current is not null && entry is null
                        && reader.LocalName.Equals(TellicoElementNames.BorrowersElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InBorrowers = false;
                    }
                    else if (current is not null && entry is null
                        && reader.LocalName.Equals(TellicoElementNames.ImagesElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InImages = false;
                    }
                    else if (current is not null && entry is null && current.PendingBorrowerName is not null
                        && reader.LocalName.Equals(TellicoElementNames.BorrowerElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.CommitBorrower();
                    }
                    else if (current is not null && entry is null && current.InLoan
                        && reader.LocalName.Equals(TellicoElementNames.LoanElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.InLoan = false;
                    }
                    else if (current is not null && entry is null && current.PendingImage is not null
                        && reader.LocalName.Equals(TellicoElementNames.ImageElement, StringComparison.OrdinalIgnoreCase))
                    {
                        current.CommitImage();
                    }
                    else if (current is not null && entry is not null
                        && reader.LocalName.Equals(TellicoElementNames.EntryElement, StringComparison.OrdinalIgnoreCase))
                    {
                        if (frames.Count > 0)
                        {
                            throw new TellicoMalformedException("entry has unclosed field elements.");
                        }

                        current.FinishEntry(entry);
                        entry = null;
                    }
                    else if (current is not null && entry is not null && frames.Count > 0)
                    {
                        var frame = frames.Peek();
                        if (!reader.LocalName.Equals(frame.Name, StringComparison.Ordinal))
                        {
                            throw new TellicoMalformedException(
                                $"mismatched element: expected closing '{frame.Name}', found '{reader.LocalName}'.");
                        }

                        frames.Pop();
                        if (frames.Count == 0)
                        {
                            current.FinishField(entry, frame);
                        }
                        else
                        {
                            frames.Peek().Children.Add(frame);
                        }
                    }

                    break;
            }
        }

        if (!rootSeen)
        {
            throw new TellicoMalformedException("XML document has no collection root element.");
        }

        if (collections.Count == 0)
        {
            throw new TellicoUnsupportedSchemaException("document contains no collection elements.");
        }

        if (collections.TrueForAll(static collection => collection.Entries.Count == 0))
        {
            throw new TellicoEmptyCollectionException();
        }

        return new TellicoParseResult(syntaxVersion, collections);
    }

    private static void AppendBounded(StringBuilder builder, string value, long maxChars, string what)
    {
        if (builder.Length + value.Length > maxChars)
        {
            throw new TellicoMalformedException($"{what} text exceeds the maximum supported length.");
        }

        builder.Append(value);
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

    private sealed class FieldFrame
    {
        public FieldFrame(string name, string? reference)
        {
            Name = name;
            Reference = reference;
        }

        public string Name { get; }

        public string? Reference { get; }

        public StringBuilder Text { get; } = new();

        public List<FieldFrame> Children { get; } = [];
    }

    private sealed class PendingImage
    {
        public PendingImage(string? id, string? format, string? width, string? height, StringBuilder text)
        {
            Id = id;
            Format = format;
            Width = width;
            Height = height;
            Text = text;
        }

        public string? Id { get; }
        public string? Format { get; }
        public string? Width { get; }
        public string? Height { get; }
        public StringBuilder Text { get; }
    }

    private sealed class CollectionState
    {
        public CollectionState(string title, string type, string? entryTitle)
        {
            Title = title;
            Type = type;
            EntryTitle = entryTitle;
        }

        public string Title { get; }
        public string Type { get; }
        public string? EntryTitle { get; }
        public bool InFields { get; set; }
        public bool InBorrowers { get; set; }
        public bool InImages { get; set; }
        public TellicoFieldDefinition? PendingField { get; set; }
        public Dictionary<string, string>? PendingProps { get; set; }
        public string? PendingPropName { get; set; }
        public StringBuilder? PendingPropText { get; set; }
        public StringBuilder? PendingBorrowerName { get; set; }
        public bool InLoan { get; set; }
        public TellicoLoan? PendingLoan { get; set; }
        public PendingImage? PendingImage { get; set; }

        public int EntryCount { get; private set; }

        private readonly Dictionary<string, TellicoFieldDefinition> fieldDefinitions = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<EntryState?> entryStates = [];
        private readonly Dictionary<string, TellicoImageReference> images = new(StringComparer.Ordinal);
        private readonly List<TellicoLoan> loans = [];
        private readonly List<string> unsupportedFields = [];
        private readonly HashSet<string> unsupportedSeen = new(StringComparer.Ordinal);
        private readonly List<string> skippedReasons = [];
        private int skippedCount;
        private int imageCounter;
        private bool notedMissingDefinitions;
        private bool notedFieldProps;

        public void CommitField()
        {
            if (PendingField is not null)
            {
                if (PendingProps is { Count: > 0 } && !notedFieldProps)
                {
                    notedFieldProps = true;
                    Unsupported("Field properties (e.g. bibtex-field names) are not mapped.");
                }

                if (PendingField.Type == TellicoElementNames.TellicoFieldType.Unknown
                    && !string.IsNullOrWhiteSpace(PendingField.RawType)
                    && unsupportedSeen.Add("unknowntype:" + PendingField.RawType))
                {
                    unsupportedFields.Add(
                        $"Field '{PendingField.Name}' has unknown type '{PendingField.RawType}'; values imported as text.");
                }

                fieldDefinitions.TryAdd(PendingField.Name, PendingField);
                PendingField = null;
            }

            PendingProps = null;
        }

        public void CommitProp()
        {
            if (PendingPropName is not null && PendingProps is not null)
            {
                PendingProps[PendingPropName] = PendingPropText?.ToString().Trim() ?? string.Empty;
            }

            PendingPropName = null;
            PendingPropText = null;
        }

        public void CommitBorrower()
        {
            var borrower = PendingBorrowerName?.ToString().Trim() ?? string.Empty;
            if (PendingLoan is not null && !string.IsNullOrWhiteSpace(borrower)
                && !string.IsNullOrWhiteSpace(PendingLoan.EntryRef))
            {
                loans.Add(PendingLoan with { Borrower = borrower });
            }

            PendingBorrowerName = null;
            PendingLoan = null;
            InLoan = false;
        }

        public void CommitImage()
        {
            if (PendingImage is null)
            {
                return;
            }

            var text = PendingImage.Text.ToString().Trim();
            string key;
            if (!string.IsNullOrWhiteSpace(PendingImage.Id))
            {
                key = PendingImage.Id.Trim();
            }
            else if (text.Length > 0 && text.Length < 256 && IsImageKey(text))
            {
                key = text;
            }
            else
            {
                if (text.Length >= 256)
                {
                    Unsupported("Embedded image data is not imported; only image metadata is retained.");
                }

                key = $"image-{++imageCounter}";
            }

            images.TryAdd(key, new TellicoImageReference(key, PendingImage.Format, PendingImage.Width, PendingImage.Height));
            PendingImage = null;
        }

        private static bool IsImageKey(string text)
        {
            foreach (var ch in text)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_' && ch != '.')
                {
                    return false;
                }
            }

            return text.Length > 0;
        }

        public void FinishField(EntryState entry, FieldFrame frame)
        {
            if (frame.Name.Equals(TellicoElementNames.IdElement, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(entry.Id))
            {
                var idText = ScalarText(frame).Trim();
                if (!string.IsNullOrWhiteSpace(idText))
                {
                    entry.Id = idText;
                }

                return;
            }

            if (!fieldDefinitions.TryGetValue(frame.Name, out var definition))
            {
                var key = "Tellico." + frame.Name;
                entry.Extra.TryAdd(key, ScalarText(frame).Trim());
                if (unsupportedSeen.Add(key))
                {
                    unsupportedFields.Add(key);
                }

                return;
            }

            AddTypedField(entry, definition, frame);
        }

        private void AddTypedField(EntryState entry, TellicoFieldDefinition definition, FieldFrame frame)
        {
            var key = definition.Name;
            switch (definition.Type)
            {
                case TellicoElementNames.TellicoFieldType.Bool:
                    var raw = ScalarText(frame).Trim();
                    if (raw.Length == 0)
                    {
                        return;
                    }

                    entry.Fields[key] = ParseFlag(raw) ? "true" : "false";
                    break;
                case TellicoElementNames.TellicoFieldType.Number:
                case TellicoElementNames.TellicoFieldType.Rating:
                    var numberText = ScalarText(frame).Trim();
                    if (numberText.Length == 0)
                    {
                        return;
                    }

                    entry.Fields[key] = TryParseCanonicalNumber(numberText, out var number) ? number : numberText;
                    break;
                case TellicoElementNames.TellicoFieldType.Date:
                    AddDateField(entry, key, frame);
                    break;
                case TellicoElementNames.TellicoFieldType.Image:
                    var imageRef = frame.Reference ?? ScalarText(frame).Trim();
                    if (imageRef.Length == 0)
                    {
                        return;
                    }

                    AddImageReference(entry, key, imageRef);
                    break;
                default:
                    AddTextField(entry, definition, key, frame);
                    break;
            }

            if (TellicoElementNames.IsImageField(definition.Name)
                && definition.Type != TellicoElementNames.TellicoFieldType.Image
                && entry.Fields.TryGetValue(key, out var candidate)
                && candidate is string reference
                && images.ContainsKey(reference))
            {
                AddImageReference(entry, key, reference);
            }
        }

        private void AddTextField(EntryState entry, TellicoFieldDefinition definition, string key, FieldFrame frame)
        {
            if (frame.Children.Count == 0)
            {
                var text = frame.Text.ToString().Trim();
                if (text.Length == 0)
                {
                    return;
                }

                entry.Fields[key] = definition.AllowMultiple ? SplitMulti(text) : (object)text;
                return;
            }

            var values = ScalarizeChildren(frame);
            if (values.Count == 0)
            {
                return;
            }

            if (values.Count == 1 && !definition.AllowMultiple)
            {
                entry.Fields[key] = values[0];
                return;
            }

            entry.Fields[key] = values.TrueForAll(static value => value is string)
                ? values.ConvertAll(static value => (string)value!)
                : values;
        }

        private void AddDateField(EntryState entry, string key, FieldFrame frame)
        {
            string? year = null;
            string? month = null;
            string? day = null;
            foreach (var child in frame.Children)
            {
                if (child.Name.Equals("year", StringComparison.OrdinalIgnoreCase)) year = ScalarText(child).Trim();
                else if (child.Name.Equals("month", StringComparison.OrdinalIgnoreCase)) month = ScalarText(child).Trim();
                else if (child.Name.Equals("day", StringComparison.OrdinalIgnoreCase)) day = ScalarText(child).Trim();
            }

            if (year is not null)
            {
                var iso = year.Trim();
                if (month is not null && day is not null
                    && int.TryParse(month, out var monthNumber) && int.TryParse(day, out var dayNumber)
                    && int.TryParse(year, out var yearNumber))
                {
                    try
                    {
                        iso = new DateTime(yearNumber, monthNumber, dayNumber).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        iso = year.Trim();
                    }
                }

                entry.Fields[key] = iso;
                return;
            }

            var text = ScalarText(frame).Trim();
            if (text.Length == 0)
            {
                return;
            }

            entry.Fields[key] = text;
            var normalized = NormalizeDate(text);
            if (normalized is not null && !normalized.Equals(text, StringComparison.Ordinal))
            {
                entry.Fields[key + "Normalized"] = normalized;
            }
        }

        private void AddImageReference(EntryState entry, string key, string value)
        {
            if (images.TryGetValue(value, out var image))
            {
                entry.Fields[key] = value;
                entry.Fields[key + "Format"] = image.Format ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(image.Width) && !string.IsNullOrWhiteSpace(image.Height))
                {
                    entry.Fields[key + "Dimensions"] = $"{image.Width}x{image.Height}";
                }
            }
            else
            {
                entry.Fields[key] = value;
                Unsupported($"image reference '{value}' has no matching images entry");
            }
        }

        private static List<object> ScalarizeChildren(FieldFrame frame)
        {
            var values = new List<object>();
            if (frame.Children.Count > 0 && frame.Children.TrueForAll(child =>
                    child.Name.Equals(frame.Children[0].Name, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var child in frame.Children)
                {
                    if (child.Children.Count == 0)
                    {
                        var childText = child.Text.ToString().Trim();
                        if (childText.Length > 0)
                        {
                            values.Add(childText);
                        }
                    }
                    else
                    {
                        var nested = ScalarizeChildren(child);
                        if (nested.Count > 0)
                        {
                            values.Add(nested);
                        }
                    }
                }

                return values;
            }

            var text = ScalarText(frame).Trim();
            if (text.Length > 0)
            {
                values.Add(text);
            }

            return values;
        }

        private static string ScalarText(FieldFrame frame)
        {
            if (frame.Children.Count == 0)
            {
                return frame.Text.ToString();
            }

            var builder = new StringBuilder();
            builder.Append(frame.Text);
            foreach (var child in frame.Children)
            {
                builder.Append(ScalarText(child));
            }

            return builder.ToString();
        }

        private void Unsupported(string note)
        {
            if (unsupportedSeen.Add("note:" + note))
            {
                unsupportedFields.Add(note);
            }
        }

        public void FinishEntry(EntryState entry)
        {
            EntryCount++;
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                entry.Id = $"entry-{entryStates.Count}";
                skippedCount++;
                skippedReasons.Add($"Entry {entryStates.Count} has no id; a provisional id was assigned.");
            }

            if (entry.Fields.Count == 0 && entry.Extra.Count == 0)
            {
                skippedCount++;
                skippedReasons.Add($"Entry '{entry.Id.Trim()}' has no fields and was skipped.");
                entryStates.Add(null);
                return;
            }

            entryStates.Add(entry);
        }

        private static void ApplyLoans(EntryState entry, List<TellicoLoan> loans)
        {
            foreach (var loan in loans)
            {
                if (loan.EntryRef.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                {
                    entry.Fields.TryAdd("Borrower", loan.Borrower);
                    if (!string.IsNullOrWhiteSpace(loan.LoanDate))
                    {
                        entry.Fields.TryAdd("LoanDate", loan.LoanDate);
                    }

                    if (!string.IsNullOrWhiteSpace(loan.DueDate))
                    {
                        entry.Fields.TryAdd("DueDate", loan.DueDate);
                    }

                    if (!string.IsNullOrWhiteSpace(loan.Uid))
                    {
                        entry.Fields.TryAdd("LoanUid", loan.Uid);
                    }
                }
            }
        }

        public TellicoCollection Complete(string? syntaxVersion)
        {
            var notes = new List<string>(unsupportedFields);
            if (syntaxVersion is not null && int.TryParse(syntaxVersion.Trim(), out var version))
            {
                if (version > TellicoElementNames.CurrentSyntaxVersion)
                {
                    notes.Add($"Syntax version {version} is newer than the verified version {TellicoElementNames.CurrentSyntaxVersion}; parsing proceeds tolerantly.");
                }
                else if (version < TellicoElementNames.MinimumSupportedSyntaxVersion)
                {
                    throw new TellicoUnsupportedSchemaException(
                        $"syntax version {version} predates the supported element/field layout (minimum {TellicoElementNames.MinimumSupportedSyntaxVersion}).");
                }
            }
            else
            {
                notes.Add("Syntax version is missing or unrecognized; parsing proceeds tolerantly.");
            }

            if (!notedMissingDefinitions && fieldDefinitions.Count == 0 && entryStates.Any(static state => state is not null))
            {
                notedMissingDefinitions = true;
                notes.Add("No field definitions found; all values imported as text.");
            }

            var entries = new List<TellicoEntry?>(entryStates.Count);
            foreach (var state in entryStates)
            {
                if (state is null)
                {
                    entries.Add(null);
                    continue;
                }

                ApplyLoans(state, loans);
                entries.Add(new TellicoEntry(
                    Index: entries.Count,
                    Id: state.Id.Trim(),
                    Fields: new Dictionary<string, object?>(state.Fields, StringComparer.Ordinal),
                    ExtraFields: new Dictionary<string, string>(state.Extra, StringComparer.Ordinal)));
            }

            return new TellicoCollection(
                Title,
                Type,
                EntryTitle,
                new List<TellicoFieldDefinition>(fieldDefinitions.Values),
                entries,
                new Dictionary<string, TellicoImageReference>(images, StringComparer.Ordinal),
                loans,
                notes,
                skippedCount,
                skippedReasons);
        }
    }

    private sealed class EntryState
    {
        public EntryState(string? id) => Id = id ?? string.Empty;

        public string Id { get; set; }

        public Dictionary<string, object?> Fields { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Extra { get; } = new(StringComparer.Ordinal);
    }

    private static TellicoFieldDefinition ReadFieldDefinition(XmlReader reader)
    {
        var name = ReadAttribute(reader, "name") ?? string.Empty;
        var rawType = ReadAttribute(reader, "type");
        return new TellicoFieldDefinition(
            Name: name,
            Title: ReadAttribute(reader, "title") ?? name,
            Category: ReadAttribute(reader, "category") ?? string.Empty,
            Type: TellicoElementNames.ParseFieldType(rawType),
            AllowMultiple: TellicoElementNames.AllowsMultiple(ReadAttribute(reader, "flags")),
            Format: ReadAttribute(reader, "format"),
            RawType: rawType);
    }

    private static bool ParseFlag(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "1" or "true" or "yes" or "y" or "on";
    }

    private static bool TryParseCanonicalNumber(string value, out object number)
    {
        number = value;
        if (!CanonicalNumber.IsMatch(value.Trim()))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Contains('.', StringComparison.Ordinal))
        {
            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble))
            {
                number = parsedDouble;
                return true;
            }

            return false;
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLong))
        {
            number = parsedLong;
            return true;
        }

        return false;
    }

    private static string? NormalizeDate(string value)
    {
        foreach (var format in DateFormats)
        {
            if (DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static object SplitMulti(string value)
    {
        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? new List<string>(parts) : (object)value;
    }
}
