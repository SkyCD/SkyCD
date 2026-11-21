using System;
using System.Collections.Generic;
using System.Globalization;

namespace SkyCD.Plugin.Office.Models;

/// <summary>
/// Metadata facts normalized from either an OOXML package or an OLE2 compound document.
/// </summary>
internal sealed record OfficeMetadata
{
    public required string Container { get; init; }

    public string? DocumentType { get; init; }

    public bool IsEncrypted { get; init; }

    public string? Title { get; init; }
    public string? Subject { get; init; }
    public string? Author { get; init; }
    public string? Keywords { get; init; }
    public string? Comments { get; init; }
    public string? Category { get; init; }
    public string? ContentStatus { get; init; }
    public string? Template { get; init; }
    public string? LastSavedBy { get; init; }
    public string? RevisionNumber { get; init; }
    public string? Language { get; init; }

    public string? ApplicationName { get; init; }
    public string? ApplicationVersion { get; init; }
    public string? Company { get; init; }
    public string? Manager { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? ModifiedAt { get; init; }
    public DateTimeOffset? LastPrintedAt { get; init; }

    public long? PageCount { get; init; }
    public long? WordCount { get; init; }
    public long? CharacterCount { get; init; }
    public long? SlideCount { get; init; }

    /// <summary>An OOXML package wrapped in the standard encryption container, which is never decrypted.</summary>
    public static OfficeMetadata Encrypted { get; } = new() { Container = "ooxml", IsEncrypted = true };

    public static OfficeMetadata FromPackage(string documentType, OfficeCoreProperties? core,
        OfficeExtendedProperties? extended) =>
        new()
        {
            Container = "ooxml",
            DocumentType = documentType,
            Title = core?.Title,
            Subject = core?.Subject,
            Author = core?.Author,
            Keywords = core?.Keywords,
            Comments = core?.Comments,
            Category = core?.Category,
            ContentStatus = core?.ContentStatus,
            LastSavedBy = core?.LastSavedBy,
            RevisionNumber = core?.RevisionNumber,
            Language = core?.Language,
            CreatedAt = core?.CreatedAt,
            ModifiedAt = core?.ModifiedAt,
            LastPrintedAt = core?.LastPrintedAt,
            ApplicationName = extended?.Application,
            ApplicationVersion = extended?.ApplicationVersion,
            Company = extended?.Company,
            Manager = extended?.Manager,
            Template = extended?.Template,
            PageCount = extended?.PageCount,
            WordCount = extended?.WordCount,
            CharacterCount = extended?.CharacterCount,
            SlideCount = extended?.SlideCount
        };

    public static OfficeMetadata FromCompound(string documentType,
        IReadOnlyDictionary<uint, object> summary,
        IReadOnlyDictionary<uint, object> documentSummary) =>
        new()
        {
            Container = "ole",
            DocumentType = documentType,
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
