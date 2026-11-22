using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Presentation.Catalog;
using Xunit;

namespace SkyCD.Plugin.Feature.Loans.Tests;

public sealed class LoansPluginGatingTests
{
    private static readonly CatalogEntry Entry = new(
        "book",
        "Book",
        @"C:\catalog\book",
        null,
        CatalogEntryKind.File,
        42,
        new Dictionary<string, string>
        {
            ["Borrower"] = "Alice",
            ["LoanDate"] = "2024-03-01"
        });

    [Fact]
    public async Task DisabledPluginLeavesImportedPropertiesPlainAndContributesNoSurfaces()
    {
        using var directory = new TemporaryDirectory();
        var catalogPath = Path.Combine(directory.Path, "catalog");
        using (var database = OpenDatabase(directory.Path))
        {
            var repository = OpenCatalog(database);
            repository.Save("book", new CatalogDocument
            {
                Id = "book",
                Name = "Book",
                Properties = new PropertiesCollection
                {
                    ["Borrower"] = "Alice",
                    ["LoanDate"] = "2024-03-01"
                }
            });

            var import = new CatalogImportExtensionManager([]);
            var outcome = await import.RunAsync([Entry], TimeSpan.FromSeconds(5));
            Assert.Empty(outcome.Warnings);
            Assert.Equal(0, outcome.AffectedEntries);

            var features = new CatalogFeatureExtensionManager([]);
            Assert.Empty(features.GetDecorations([Entry]));
            Assert.Empty(features.GetDetailsColumns());
            Assert.Empty(features.GetAdvancedSearchFields());
            Assert.Empty(features.GetEntryCommands());
            Assert.False(await features.ExecuteEntryCommandAsync("skycd.loans.lend", Entry));

            Assert.Equal("Alice", repository.Get("book")!.Properties["Borrower"]);
            Assert.Equal("2024-03-01", repository.Get("book")!.Properties["LoanDate"]);
        }

        using var reopened = OpenDatabase(directory.Path);
        var loans = CatalogLoanService.Open(OpenCatalog(reopened));
        loans.CatalogPath = catalogPath;
        Assert.Empty(loans.GetHistory("book"));
        Assert.Empty(loans.GetBorrowers());
    }

    [Fact]
    public async Task EnabledPluginCreatesLoansAndRestoresSurfacesAfterReEnable()
    {
        using var directory = new TemporaryDirectory();
        using var database = OpenDatabase(directory.Path);
        var repository = OpenCatalog(database);
        repository.Save("book", new CatalogDocument { Id = "book", Name = "Book" });
        var provider = new TestProvider(repository);

        var plugin = new LoansPlugin();
        plugin.InitializeCatalogRepository(provider);
        plugin.OnCatalogLifecycle(CatalogLifecycle.Opened, provider.CatalogPath);
        var outcome = await new CatalogImportExtensionManager([plugin])
            .RunAsync([Entry], TimeSpan.FromSeconds(5));
        Assert.Empty(outcome.Warnings);
        Assert.Equal(1, outcome.AffectedEntries);

        var features = new CatalogFeatureExtensionManager([plugin]);
        var decoration = Assert.Single(features.GetDecorations([Entry]));
        Assert.Equal("book", decoration.EntryId);
        Assert.False(decoration.Attention);
        Assert.Equal("Alice", decoration.Tooltip);

        var column = Assert.Single(features.GetDetailsColumns());
        Assert.Equal("Alice", column.SelectValue(Entry));
        Assert.Equal(3, features.GetEntryCommands().Count);

        var searchFields = features.GetAdvancedSearchFields();
        Assert.Equal(3, searchFields.Count);
        var activeField = Assert.Single(searchFields, field => field.Key == "skycd.loans.active");
        Assert.True(activeField.Matches(Entry));
        var overdueField = Assert.Single(searchFields, field => field.Key == "skycd.loans.overdue");
        Assert.False(overdueField.Matches(Entry));
        var borrowerField = Assert.Single(searchFields, field => field.Key == "skycd.loans.borrower");
        Assert.Equal("Alice", borrowerField.SelectValue?.Invoke(Entry));

        var reEnabled = new LoansPlugin();
        reEnabled.InitializeCatalogRepository(provider);
        reEnabled.OnCatalogLifecycle(CatalogLifecycle.Opened, provider.CatalogPath);
        var restored = new CatalogFeatureExtensionManager([reEnabled]);
        Assert.Single(restored.GetDecorations([Entry]));
        Assert.Single(restored.GetDetailsColumns());

        Assert.True(await features.ExecuteEntryCommandAsync("skycd.loans.return", Entry));
        Assert.Empty(features.GetDecorations([Entry]));
        Assert.Null(column.SelectValue(Entry));

        var service = CatalogLoanService.Open(repository);
        service.CatalogPath = provider.CatalogPath!;
        var history = Assert.Single(service.GetHistory("book"));
        Assert.False(history.IsActive);
    }

    private static Database OpenDatabase(string directory) =>
        new("loans-gating", new DatabaseConfiguration { Directory = directory });

    private static CatalogDocumentRepository OpenCatalog(Database database)
    {
        var collection = database.GetDefaultCollection();
        var repository = new CatalogDocumentRepository();
        repository.Initialize(typeof(CatalogDocument), collection.Name, collection);
        return repository;
    }

    private sealed class TestProvider(CatalogDocumentRepository repository) : ICatalogRepositoryProvider
    {
        public CatalogDocumentRepository? Repository => repository;

        public string? CatalogPath => "test-catalog";

        public event EventHandler<CatalogLifecycle>? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("skycd-loans-gating-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
