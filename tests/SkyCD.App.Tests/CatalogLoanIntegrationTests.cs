using System;
using System.IO;
using System.Linq;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Lending;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.Staging;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class CatalogLoanIntegrationTests
{
    [Fact]
    public void SourceFixturesPreserveDatePrecisionHistoryAndProvenanceAcrossReopen()
    {
        using var payload = System.Text.Json.JsonDocument.Parse("""
            {"nodes":[
              {"nodeId":"gcstar","name":"The Matrix","kind":"media","properties":{
                "Borrower":"Alice","LendDate":"2024-03-01",
                "Borrowings":"Alice | 2024-03-01 |  | Bob | 2023-01-05 | 2023-01-20 |"}},
              {"nodeId":"tellico","name":"Tellico item","kind":"entry","properties":{
                "Borrower":"Alice","LoanDate":"2024-01-05","DueDate":"2024-02-05","LoanUid":"u1"}},
              {"nodeId":"unmapped","name":"Undated item","kind":"entry","properties":{"DueDate":"invalid","LoanUid":"orphan"}}
            ]}
            """);
        var entries = CatalogImportTests.Extract(payload.RootElement);
        using var directory = new TemporaryDirectory();
        using (var database = OpenDatabase(directory.Path))
        {
            var catalog = OpenCatalog(database);
            foreach (var entry in entries)
            {
                catalog.Save(entry.Id, entry);
            }

            var service = CatalogLoanService.Open(catalog);
            var mapper = new ImportedLoanMapper(service);
            var warning = Assert.Single(mapper.Import(entries));
            Assert.Contains("DueDate", warning);
            Assert.Contains("LoanUid", warning);
            mapper.Import(entries);
            Assert.Equal(2, service.GetHistory("gcstar").Count);
            Assert.Single(service.GetHistory("tellico"));

            var editor = new LoanDialogViewModel(service, "tellico", () => { });
            editor.MarkReturnedCommand.Execute(null);
            Assert.Empty(editor.Error);
            mapper.Import(entries);
        }

        using var reopened = OpenDatabase(directory.Path);
        var repository = OpenCatalog(reopened);
        var lending = CatalogLoanService.Open(repository);
        var gcstar = lending.GetHistory("gcstar");
        Assert.Contains(gcstar, loan => loan.IsActive && loan.LoanDate == "2024-03-01");
        Assert.Contains(gcstar, loan => !loan.IsActive && loan.ReturnDate == "2023-01-20");
        var tellico = Assert.Single(lending.GetHistory("tellico"));
        Assert.Equal("2024-01-05", tellico.LoanDate);
        Assert.Equal("2024-02-05", tellico.DueDate);
        Assert.Equal("u1", tellico.SourceId);
        Assert.False(tellico.IsActive);
        Assert.Equal("u1", repository.Get("tellico")!.Properties["LoanUid"]);
        Assert.Equal(2, lending.GetBorrowers().Count);
        Assert.Empty(lending.GetHistory("unmapped"));
    }

    [Fact]
    public void SyncRemapsScannedIdsAndPreservesLoansAndRenamedBorrowerReferences()
    {
        using var directory = new TemporaryDirectory();
        using var database = OpenDatabase(directory.Path);
        var catalog = OpenCatalog(database);
        var original = new CatalogDocument { Id = "existing", Name = "Disk" };
        catalog.Save(original.Id, original);
        var service = CatalogLoanService.Open(catalog);
        var editor = new LoanDialogViewModel(service, original.Id, () => { })
        {
            ContactName = "Alice",
            LoanDate = "2024-04-01",
            DueDate = "2024-04-02",
            Note = "Box included"
        };
        editor.AddBorrowerCommand.Execute(null);
        var borrowerId = Assert.Single(service.GetBorrowers()).Id;
        editor.MarkLentCommand.Execute(null);
        editor.ContactName = "Alex";
        editor.RenameBorrowerCommand.Execute(null);
        Assert.Empty(editor.Error);

        var scan = new CatalogDocument { Id = "scan", Name = original.Name, Size = 100 };
        var merged = Assert.Single(new CatalogEntryMerger().Merge([scan], [original]));
        catalog.Save(merged.Id, merged);
        var active = service.GetActiveLoans(new DateOnly(2024, 4, 3));
        Assert.Equal(("Alex", true), active[merged.Id]);
        var history = Assert.Single(service.GetHistory(merged.Id));
        Assert.Equal(borrowerId, history.BorrowerId);
        Assert.Equal("Box included", history.Note);
        Assert.False(history.IsOverdue(new DateOnly(2024, 4, 2)));
        Assert.Equal(100, catalog.Get(merged.Id)!.Size);

        merged.SetLendingState(active[merged.Id]);
        var index = new CatalogSearchIndex();
        index.Rebuild([merged, new CatalogDocument { Id = "other", Name = "Other disk" }]);
        foreach (var predicate in new[]
        {
            new AdvancedCatalogPredicate { Field = AdvancedCatalogField.ActiveLoan, Operator = AdvancedCatalogOperator.IsPresent },
            new AdvancedCatalogPredicate { Field = AdvancedCatalogField.OverdueLoan, Operator = AdvancedCatalogOperator.IsPresent },
            new AdvancedCatalogPredicate { Field = AdvancedCatalogField.Borrower, Operator = AdvancedCatalogOperator.Equals, Value = "Alex" }
        })
        {
            var results = AdvancedCatalogSearchService.Search(index, new AdvancedCatalogQuery
            {
                Root = new CatalogQueryGroup { Predicates = [predicate] }
            });
            Assert.Equal(merged.Id, Assert.Single(results).Document.Id);
        }
    }

    [Fact]
    public void ContactsPersistLoanLinksAndVCardDetailsAcrossReopen()
    {
        using var directory = new TemporaryDirectory();
        string contactId;
        const string itemId = "album";
        using (var database = OpenDatabase(directory.Path))
        {
            var catalog = OpenCatalog(database);
            catalog.Save(itemId, new CatalogDocument { Id = itemId, Name = "Album" });
            var service = CatalogLoanService.Open(catalog);
            var contact = service.CreateContact("Alice");
            contactId = contact.Id;
            contact.DisplayName = "Alice Example";
            contact.Email = "alice@example.com";
            contact.Company = "ACME";
            contact.Phone = "+1 555 0100";
            contact.Address = "1 Main St";
            service.SaveContact(contact);
            service.Lend(itemId, contactId, new DateOnly(2024, 5, 1), new DateOnly(2024, 5, 10), "Box");

            var active = Assert.Single(service.GetActiveLoansForContact(contactId));
            Assert.Equal("Album", service.GetEntryName(active.EntryId));
            Assert.Contains("FN:Alice Example", service.ExportContacts(), StringComparison.Ordinal);
            Assert.Throws<ContactEditException>(() => service.DeleteContact(contactId));
        }

        using (var reopened = OpenDatabase(directory.Path))
        {
            var catalog = OpenCatalog(reopened);
            var service = CatalogLoanService.Open(catalog);
            var contact = Assert.Single(service.GetContacts());
            Assert.Equal(contactId, contact.Id);
            Assert.Equal("Alice Example", contact.DisplayName);
            Assert.Equal("alice@example.com", contact.Email);
            Assert.Equal("ACME", contact.Company);
            Assert.Equal("+1 555 0100", contact.Phone);
            Assert.Equal("1 Main St", contact.Address);

            var active = Assert.Single(service.GetActiveLoansForContact(contactId));
            Assert.Equal(itemId, active.EntryId);
            Assert.Equal("Album", service.GetEntryName(itemId));
        }
    }

    [Fact]
    public void VCardImportMergesByEmailAndUnusedContactsStayDeletable()
    {
        using var directory = new TemporaryDirectory();
        using var database = OpenDatabase(directory.Path);
        var service = CatalogLoanService.Open(OpenCatalog(database));

        const string original = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Alice
            EMAIL:alice@example.com
            TEL;TYPE=VOICE:+1 555 0100
            END:VCARD
            """;
        const string updated = """
            BEGIN:VCARD
            VERSION:3.0
            FN:Alice Example
            EMAIL:alice@example.com
            TEL;TYPE=VOICE:+1 555 0200
            ORG:ACME
            END:VCARD
            """;

        Assert.Equal(new ContactImportResult(1, 0), service.ImportContacts(VCardCodec.Parse(original)));
        Assert.Equal(new ContactImportResult(0, 1), service.ImportContacts(VCardCodec.Parse(updated)));

        var contact = Assert.Single(service.GetContacts());
        Assert.Equal("Alice Example", contact.DisplayName);
        Assert.Equal("+1 555 0200", contact.Phone);
        Assert.Equal("ACME", contact.Company);
        Assert.Contains("TEL;TYPE=VOICE:+1 555 0200", service.ExportContacts(), StringComparison.Ordinal);
        Assert.Throws<ContactEditException>(() => service.CreateContact("alice"));

        service.DeleteContact(contact.Id);
        Assert.Empty(service.GetContacts());
    }

    private static Database OpenDatabase(string directory) =>
        new("lending", new DatabaseConfiguration { Directory = directory });

    private static CatalogDocumentRepository OpenCatalog(Database database)
    {
        var collection = database.GetDefaultCollection();
        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
        return repository;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("skycd-loans-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
