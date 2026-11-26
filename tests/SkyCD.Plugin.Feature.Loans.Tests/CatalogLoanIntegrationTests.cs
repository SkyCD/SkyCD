using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Feature.Loans.Exceptions;
using SkyCD.Plugin.Feature.Loans.Import;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Feature.Loans.Parsing;
using SkyCD.Plugin.Feature.Loans.Views;
using SkyCD.Presentation.Staging;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.Loans.Tests;

public sealed class CatalogLoanIntegrationTests
{
    [Fact]
    public void SourceFixturesPreserveDatePrecisionHistoryAndProvenanceAcrossReopen()
    {
        var entries = new[]
        {
            Document("gcstar", "The Matrix",
                ("Borrower", "Alice"),
                ("LendDate", "2024-03-01"),
                ("Borrowings", "Alice | 2024-03-01 |  | Bob | 2023-01-05 | 2023-01-20 |")),
            Document("tellico", "Tellico item",
                ("Borrower", "Alice"),
                ("LoanDate", "2024-01-05"),
                ("DueDate", "2024-02-05"),
                ("LoanUid", "u1")),
            Document("unmapped", "Undated item",
                ("DueDate", "invalid"),
                ("LoanUid", "orphan"))
        };
        using var workspace = new TestWorkspace();
        using (var database = OpenDatabase(workspace.Root))
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

        using var reopened = OpenDatabase(workspace.Root);
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
    public async Task SyncRemapsScannedIdsAndPreservesLoansAndRenamedBorrowerReferences()
    {
        using var workspace = new TestWorkspace();
        using var database = OpenDatabase(workspace.Root);
        var catalog = OpenCatalog(database);
        var original = new CatalogDocument { Id = "existing", Name = "Disk" };
        catalog.Save(original.Id, original);
        var service = CatalogLoanService.Open(catalog);
        var contacts = new ContactsDialogViewModel(service);
        var editor = new LoanDialogViewModel(service, original.Id, () => { }, () =>
        {
            contacts.AddContactCommand.Execute(null);
            contacts.ContactName = "Alice";
            contacts.SaveContactCommand.Execute(null);
            return Task.CompletedTask;
        })
        {
            LoanDate = "2024-04-01",
            DueDate = "2024-04-02",
            Note = "Box included"
        };
        Assert.Empty(editor.Borrowers);
        await editor.ManageBorrowersCommand.ExecuteAsync(null);
        Assert.Empty(contacts.Error);
        var borrower = Assert.Single(editor.Borrowers);
        Assert.Equal("Alice", borrower.Name);
        editor.SelectedBorrower = borrower;
        editor.MarkLentCommand.Execute(null);
        Assert.Empty(editor.Error);
        contacts.ContactName = "Alex";
        contacts.SaveContactCommand.Execute(null);
        Assert.Empty(contacts.Error);

        var scan = new CatalogDocument { Id = "scan", Name = original.Name, Size = 100 };
        var merged = Assert.Single(new CatalogEntryMerger().Merge([scan], [original]));
        catalog.Save(merged.Id, merged);
        var active = service.GetActiveLoans(new DateOnly(2024, 4, 3));
        Assert.Equal(("Alex", true), active[merged.Id]);
        var history = Assert.Single(service.GetHistory(merged.Id));
        Assert.Equal(borrower.Id, history.BorrowerId);
        Assert.Equal("Box included", history.Note);
        Assert.False(history.IsOverdue(new DateOnly(2024, 4, 2)));
        Assert.Equal(100, catalog.Get(merged.Id)!.Size);
    }

    [Fact]
    public void ContactsPersistLoanLinksAndVCardDetailsAcrossReopen()
    {
        using var workspace = new TestWorkspace();
        string contactId;
        const string itemId = "album";
        using (var database = OpenDatabase(workspace.Root))
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

        using (var reopened = OpenDatabase(workspace.Root))
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
        using var workspace = new TestWorkspace();
        using var database = OpenDatabase(workspace.Root);
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
        Assert.Contains("TEL;TYPE=VOICE:+1 555 0200", service.ExportContacts(), StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ContactEditException>(() => service.CreateContact("alice"));

        service.DeleteContact(contact.Id);
        Assert.Empty(service.GetContacts());
    }

    private static CatalogDocument Document(string id, string name, params (string Key, string Value)[] properties)
    {
        var collection = new PropertiesCollection();
        foreach (var (key, value) in properties)
        {
            collection[key] = value;
        }

        return new CatalogDocument { Id = id, Name = name, Properties = collection };
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
}
