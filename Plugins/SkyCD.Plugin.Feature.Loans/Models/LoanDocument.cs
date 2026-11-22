using System;
using System.Globalization;
using SkyCD.Couchbase.Attributes;

namespace SkyCD.Plugin.Feature.Loans.Models;

[CouchbaseDocument("loans", repositoryType: typeof(LoanDocumentRepository))]
public sealed class LoanDocument
{
    [Id] public string Id { get; init; } = string.Empty;

    public string CatalogPath { get; set; } = string.Empty;

    public string EntryId { get; init; } = string.Empty;

    public string BorrowerId { get; init; } = string.Empty;

    public string? LoanDate { get; init; }

    public string? DueDate { get; init; }

    public string? ReturnDate { get; set; }

    public string? Note { get; init; }

    public string? SourceId { get; init; }

    public bool IsActive => ReturnDate is null;

    public bool IsOverdue(DateOnly today) =>
        IsActive && TryReadDate(DueDate, out var due) && due < today;

    public static bool TryReadDate(string? value, out DateOnly date)
    {
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (value is { Length: > 10 } && value[10] is 'T' or ' ' &&
            DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return true;
        }

        date = default;
        return false;
    }
}
