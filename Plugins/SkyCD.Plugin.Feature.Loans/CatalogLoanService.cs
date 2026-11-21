using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents;
using SkyCD.Documents.Repository;

namespace SkyCD.Plugin.Feature.Loans;

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

    public IReadOnlyList<BorrowerDocument> GetContacts() => GetBorrowers();

    public BorrowerDocument? GetContact(string id) => borrowers.Get(id);

    public bool IsContactInUse(string contactId) => loans.GetAll()
        .Any(loan => string.Equals(loan.BorrowerId, contactId, StringComparison.Ordinal));

    public string GetEntryName(string entryId) => catalog.Get(entryId)?.Name ?? entryId;

    public IReadOnlyList<LoanDocument> GetActiveLoansForContact(string contactId) => loans.GetAll()
        .Where(loan => loan.IsActive && string.Equals(loan.BorrowerId, contactId, StringComparison.Ordinal) &&
            IsInCatalog(loan))
        .OrderBy(loan => loan.DueDate ?? "9999-99-99", StringComparer.Ordinal)
        .ThenBy(loan => loan.Id, StringComparer.Ordinal)
        .ToArray();

    internal BorrowerDocument CreateContact(string name)
    {
        BorrowerDocument? result = null;
        borrowers.Collection.Database.InBatch(() =>
        {
            var trimmed = name.Trim();
            if (trimmed.Length == 0 || GetBorrowers().Any(contact =>
                string.Equals(contact.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ContactEditException();
            }

            result = new BorrowerDocument { Id = Guid.NewGuid().ToString("N"), Name = trimmed };
            borrowers.Save(result.Id, result);
        });
        return result!;
    }

    internal void SaveContact(BorrowerDocument contact)
    {
        borrowers.Collection.Database.InBatch(() =>
        {
            var name = contact.Name.Trim();
            if (string.IsNullOrWhiteSpace(contact.Id) || name.Length == 0 || borrowers.Get(contact.Id) is null ||
                GetBorrowers().Any(other =>
                    other.Id != contact.Id && string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ContactEditException();
            }

            contact.Name = name;
            TrimFields(contact);
            borrowers.Save(contact.Id, contact);
        });
    }

    internal void DeleteContact(string id)
    {
        borrowers.Collection.Database.InBatch(() =>
        {
            if (string.IsNullOrWhiteSpace(id) || borrowers.Get(id) is null || IsContactInUse(id))
            {
                throw new ContactEditException();
            }

            using var document = borrowers.Collection.GetDocument(id);
            if (document is not null)
            {
                borrowers.Collection.Delete(document);
            }
        });
    }

    internal ContactImportResult ImportContacts(IReadOnlyList<BorrowerDocument> contacted)
    {
        var added = 0;
        var updated = 0;
        var existing = GetBorrowers().ToList();
        borrowers.Collection.Database.InBatch(() =>
        {
            foreach (var contact in contacted)
            {
                var name = contact.Name.Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                contact.Name = name;
                TrimFields(contact);
                var match = FindContact(existing, contact);
                if (match is null)
                {
                    borrowers.Save(contact.Id, contact);
                    existing.Add(contact);
                    added++;
                    continue;
                }

                MergeContact(match, contact);
                borrowers.Save(match.Id, match);
                updated++;
            }
        });
        return new ContactImportResult(added, updated);
    }

    internal string ExportContacts() => VCardCodec.Write(GetBorrowers());

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

    private static BorrowerDocument? FindContact(IReadOnlyList<BorrowerDocument> contacts, BorrowerDocument incoming)
    {
        if (!string.IsNullOrWhiteSpace(incoming.Email))
        {
            var byEmail = contacts.FirstOrDefault(contact =>
                string.Equals(contact.Email, incoming.Email, StringComparison.OrdinalIgnoreCase));
            if (byEmail is not null)
            {
                return byEmail;
            }
        }

        return contacts.FirstOrDefault(contact =>
            string.Equals(contact.Name, incoming.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static void MergeContact(BorrowerDocument target, BorrowerDocument source)
    {
        target.DisplayName = Prefer(source.DisplayName, target.DisplayName);
        target.Comments = Prefer(source.Comments, target.Comments);
        target.Phone = Prefer(source.Phone, target.Phone);
        target.Fax = Prefer(source.Fax, target.Fax);
        target.Email = Prefer(source.Email, target.Email);
        target.WebPage = Prefer(source.WebPage, target.WebPage);
        target.Messenger = Prefer(source.Messenger, target.Messenger);
        target.Address = Prefer(source.Address, target.Address);
        target.Company = Prefer(source.Company, target.Company);
        target.Position = Prefer(source.Position, target.Position);
    }

    private static string Prefer(string incoming, string existing) => incoming.Length > 0 ? incoming : existing;

    private static void TrimFields(BorrowerDocument contact)
    {
        contact.DisplayName = contact.DisplayName.Trim();
        contact.Comments = contact.Comments.Trim();
        contact.Phone = contact.Phone.Trim();
        contact.Fax = contact.Fax.Trim();
        contact.Email = contact.Email.Trim();
        contact.WebPage = contact.WebPage.Trim();
        contact.Messenger = contact.Messenger.Trim();
        contact.Address = contact.Address.Trim();
        contact.Company = contact.Company.Trim();
        contact.Position = contact.Position.Trim();
    }
}
