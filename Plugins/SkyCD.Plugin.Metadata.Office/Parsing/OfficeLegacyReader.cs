using System;
using System.Collections.Generic;
using System.Globalization;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads the properties a legacy Office document keeps in its OLE summary property sets.
/// </summary>
/// <remarks>
/// <para>
/// Word, Excel and PowerPoint wrote their document properties into the <c>SummaryInformation</c> and
/// <c>DocumentSummaryInformation</c> streams long before the package formats existed, and those streams are the
/// only place several published fields appear for a legacy document. The reading library exposes legacy
/// documents through a projection of the modern package, which omits the template name, the saving application,
/// the recorded page, word and character counts and the content status and language, and reaching its projection
/// costs a full conversion of the document rather than a read of two small streams. Since a scan opens every
/// file in a collection, this plugin reads the property sets here and keeps the values byte-identical to the ones
/// it published before the library was adopted.
/// </para>
/// <para>
/// The document type comes from the stream the application writes for its own content, so a workbook stored with
/// a Word extension is still published as <c>excel</c>; the extension only decides the type for a container whose
/// content stream this plugin does not model, such as a Visio stencil.
/// </para>
/// </remarks>
internal static class OfficeLegacyReader
{
    private static readonly IReadOnlyDictionary<uint, object> NoValues = new Dictionary<uint, object>();

    public static OfficeMetadata Read(
        CompoundFile compound,
        OfficePluginOptions options,
        OfficeFamily? hint,
        string? extension)
    {
        ArgumentNullException.ThrowIfNull(compound);
        ArgumentNullException.ThrowIfNull(options);

        // Office 2007 and later protect a package by wrapping it, unchanged, in a compound file.
        if (OfficeSourceReader.IsEncryptedWrapper(compound))
        {
            return OfficeMetadata.Protected(
                OfficeContainers.Ooxml, "the document is stored inside an encrypted package wrapper");
        }

        var documentType = DetectDocumentType(compound) ?? (hint is { } family ? OfficeFamilyResolver.DocumentType(family) : null)
            ?? throw new OfficeDecodeException(
                "the compound document does not contain a Microsoft Office document stream");

        var summary = ReadPropertyStream(compound, OfficeSourceReader.SummaryStream, options);
        var documentSummary = ReadPropertyStream(compound, OfficeSourceReader.DocumentSummaryStream, options);

        return new OfficeMetadata
        {
            Container = OfficeContainers.Ole,
            DocumentType = documentType,
            IsTemplate = OfficeFamilyResolver.IsTemplate(extension),
            Title = Text(summary, 2),
            Subject = Text(summary, 3),
            Author = Text(summary, 4),
            Keywords = Text(summary, 5),
            Comments = Text(summary, 6),
            Template = Text(summary, 7),
            LastSavedBy = Text(summary, 8),
            RevisionNumber = Text(summary, 9),
            LastPrintedAt = Timestamp(summary, 11),
            CreatedAt = Timestamp(summary, 12),
            ModifiedAt = Timestamp(summary, 13),
            PageCount = Integer(summary, 14),
            WordCount = Integer(summary, 15),
            CharacterCount = Integer(summary, 16),
            ApplicationName = Text(summary, 18),
            Category = Text(documentSummary, 2),
            Manager = Text(documentSummary, 14),
            Company = Text(documentSummary, 15),
            ContentStatus = Text(documentSummary, 27),
            Language = Text(documentSummary, 28)
        };
    }

    private static string? DetectDocumentType(CompoundFile compound)
    {
        if (compound.ContainsStream("WordDocument"))
        {
            return "word";
        }

        if (compound.ContainsStream("Workbook") || compound.ContainsStream("Book"))
        {
            return "excel";
        }

        return compound.ContainsStream("PowerPoint Document") ? "powerpoint" : null;
    }

    private static IReadOnlyDictionary<uint, object> ReadPropertyStream(
        CompoundFile compound,
        string name,
        OfficePluginOptions options)
    {
        try
        {
            return compound.TryReadStream(name, options.MaxMetadataPartBytes, out var stream)
                ? OfficePropertySet.Parse(stream)
                : NoValues;
        }
        catch (OfficeDecodeException)
        {
            // The summary is optional: a damaged chain costs metadata, not the whole extraction.
            return NoValues;
        }
    }

    private static string? Text(IReadOnlyDictionary<uint, object> values, uint propertyId)
    {
        if (!values.TryGetValue(propertyId, out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            long number => number.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    private static long? Integer(IReadOnlyDictionary<uint, object> values, uint propertyId) =>
        values.TryGetValue(propertyId, out var value) && value is long number && number > 0 ? number : null;

    private static DateTimeOffset? Timestamp(IReadOnlyDictionary<uint, object> values, uint propertyId) =>
        values.TryGetValue(propertyId, out var value) && value is DateTimeOffset timestamp ? timestamp : null;
}
