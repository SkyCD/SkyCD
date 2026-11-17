using System.Collections.Generic;

namespace SkyCD.Plugin.Tellico;

/// <summary>
/// One parsed Tellico entry. Values are typed generically: text stays text,
/// canonical numbers stay numeric, yes/no flags become "true"/"false", dates
/// keep raw text plus an ISO twin, and multi-value fields become string lists.
/// </summary>
public sealed record TellicoEntry(
    int Index,
    string Id,
    Dictionary<string, object?> Fields,
    Dictionary<string, string> ExtraFields);

/// <summary>
/// Definition of a collection field from the document's fields section.
/// </summary>
public sealed record TellicoFieldDefinition(
    string Name,
    string Title,
    string Category,
    TellicoElementNames.TellicoFieldType Type,
    bool AllowMultiple,
    string? Format,
    string? RawType);

/// <summary>
/// Parsed image reference: metadata from the XML plus bounded raw bytes
/// from the collection archive when available.
/// </summary>
public sealed record TellicoImageReference(
    string Id,
    string? Format,
    string? Width,
    string? Height);

/// <summary>
/// A parsed Tellico collection: its identity, field definitions, entries,
/// images, loans, and diagnostics.
/// </summary>
public sealed record TellicoCollection(
    string Title,
    string Type,
    string? EntryTitle,
    IReadOnlyList<TellicoFieldDefinition> FieldDefinitions,
    IReadOnlyList<TellicoEntry?> Entries,
    IReadOnlyDictionary<string, TellicoImageReference> Images,
    IReadOnlyList<TellicoLoan> Loans,
    IReadOnlyList<string> UnsupportedFields,
    int SkippedEntriesCount,
    IReadOnlyList<string> SkippedReasons);

public sealed record TellicoLoan(
    string Borrower,
    string EntryRef,
    string? LoanDate,
    string? DueDate,
    string? Uid);

/// <summary>
/// Result of parsing a Tellico collection document.
/// </summary>
public sealed record TellicoParseResult(
    string? SyntaxVersion,
    IReadOnlyList<TellicoCollection> Collections);
