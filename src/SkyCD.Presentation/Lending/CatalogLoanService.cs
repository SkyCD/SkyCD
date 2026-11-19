using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Presentation.Lending;

public sealed class CatalogLoanService(
    CatalogDocumentRepository catalog,
    BorrowerDocumentRepository borrowers,
    LoanDocumentRepository loans)
{
    public string CatalogPath { get; set; } = string.Empty;

    public static CatalogLoanService Open(CatalogDocumentRepository catalog)
    {
        var database = catalog.Collection.Database;
        var borrowers = new BorrowerDocumentRepository();
        var loans = new LoanDocumentRepository();
        OpenCollection(typeof(BorrowerDocument), borrowers.Initialize);
        OpenCollection(typeof(LoanDocument), loans.Initialize);
        return new CatalogLoanService(catalog, borrowers, loans);

        void OpenCollection(Type model, Action<Type, string, global::Couchbase.Lite.Collection> initialize)
        {
            var mapping = (CouchbaseDocument)Attribute.GetCustomAttribute(model, typeof(CouchbaseDocument))!;
            var collection = database.GetCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName)
                ?? database.CreateCollection(mapping.CollectionName, global::Couchbase.Lite.Collection.DefaultScopeName);
            initialize(model, mapping.CollectionName, collection);
        }
    }

    private static StringComparison CatalogPathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private bool IsInCatalog(LoanDocument loan) =>
        string.Equals(loan.CatalogPath, CatalogPath, CatalogPathComparison);

    internal void SaveUnderPath(string destination)
    {
        if (string.Equals(CatalogPath, destination, CatalogPathComparison))
        {
            return;
        }

        loans.Collection.Database.InBatch(() =>
        {
            var records = loans.GetAll();
            var firstSave = string.IsNullOrEmpty(CatalogPath);
            foreach (var loan in records.Where(IsInCatalog))
            {
                if (firstSave)
                {
                    loan.CatalogPath = destination;
                    Save(loan);
                    continue;
                }

                var targetKey = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    loan.Id, OperatingSystem.IsWindows() ? destination.ToUpperInvariant() : destination
                });
                var id = "copy-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(targetKey)));
                Save(new LoanDocument
                {
                    Id = id,
                    CatalogPath = destination,
                    EntryId = loan.EntryId,
                    BorrowerId = loan.BorrowerId,
                    LoanDate = loan.LoanDate,
                    DueDate = loan.DueDate,
                    ReturnDate = loan.ReturnDate,
                    Note = loan.Note,
                    SourceId = loan.SourceId
                });
            }
        });
    }

    public IReadOnlyList<BorrowerDocument> GetBorrowers() => borrowers.GetAll()
        .OrderBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public IReadOnlyList<LoanDocument> GetHistory(string entryId) => loans.GetAll()
        .Where(loan => loan.EntryId == entryId && IsInCatalog(loan))
        .OrderByDescending(loan => loan.IsActive)
        .ThenByDescending(loan => loan.LoanDate, StringComparer.Ordinal)
        .ThenBy(loan => loan.Id, StringComparer.Ordinal)
        .ToArray();

    public IReadOnlyDictionary<string, (string Borrower, bool Overdue)> GetActiveLoans(DateOnly today)
    {
        var names = GetBorrowers().ToDictionary(contact => contact.Id, contact => contact.Name, StringComparer.Ordinal);
        return loans.GetAll().Where(loan => loan.IsActive && IsInCatalog(loan))
            .GroupBy(loan => loan.EntryId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group =>
            {
                var active = group.OrderByDescending(loan => loan.LoanDate, StringComparer.Ordinal)
                    .ThenBy(loan => loan.Id, StringComparer.Ordinal).First();
                return (names.GetValueOrDefault(active.BorrowerId, string.Empty), group.Any(loan => loan.IsOverdue(today)));
            }, StringComparer.Ordinal);
    }

    internal BorrowerDocument AddBorrower(string name)
    {
        BorrowerDocument? result = null;
        borrowers.Collection.Database.InBatch(() => result = FindOrCreateBorrower(name));
        return result!;
    }

    internal void RenameBorrower(string id, string name)
    {
        borrowers.Collection.Database.InBatch(() =>
        {
            var contact = borrowers.Get(id) ?? throw new LoanEditException();
            var trimmed = name.Trim();
            if (trimmed.Length == 0 || GetBorrowers().Any(other => other.Id != id &&
                string.Equals(other.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                throw new LoanEditException();
            }

            contact.Name = trimmed;
            borrowers.Save(id, contact);
        });
    }

    internal void Lend(string entryId, string borrowerId, DateOnly date, DateOnly? due, string? note)
    {
        loans.Collection.Database.InBatch(() =>
        {
            if (catalog.Get(entryId) is null || borrowers.Get(borrowerId) is null ||
                due < date || GetHistory(entryId).Any(loan => loan.IsActive))
            {
                throw new LoanEditException();
            }

            Save(new LoanDocument
            {
                Id = Guid.NewGuid().ToString("N"),
                CatalogPath = CatalogPath,
                EntryId = entryId,
                BorrowerId = borrowerId,
                LoanDate = FormatDate(date),
                DueDate = due is { } dueDate ? FormatDate(dueDate) : null,
                Note = note?.Trim()
            });
        });
    }

    internal void Return(string entryId, string loanId, DateOnly date)
    {
        loans.Collection.Database.InBatch(() =>
        {
            var loan = loans.Get(loanId);
            if (loan is null || loan.EntryId != entryId || !IsInCatalog(loan) || !loan.IsActive ||
                (LoanDocument.TryReadDate(loan.LoanDate, out var lent) && date < lent))
            {
                throw new LoanEditException();
            }

            loan.ReturnDate = FormatDate(date);
            Save(loan);
        });
    }

    internal void Save(LoanDocument loan) => loans.Save(loan.Id, loan);

    internal BorrowerDocument FindOrCreateBorrower(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new LoanEditException();
        }

        var existing = GetBorrowers().FirstOrDefault(contact =>
            string.Equals(contact.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var contact = new BorrowerDocument { Id = Guid.NewGuid().ToString("N"), Name = trimmed };
        borrowers.Save(contact.Id, contact);
        return contact;
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
