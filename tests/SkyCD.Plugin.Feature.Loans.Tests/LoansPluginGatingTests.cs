using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Catalog;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Host.Catalog;
using SkyCD.Presentation.Catalog;
using SkyCD.TestSupport;
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
    public async Task AuxiliaryData_ReportsImportedLoansAndBorrowers()
    {
        using var workspace = new TestWorkspace();
        using var database = OpenDatabase(workspace.Root);
        var repository = OpenCatalog(database);
        repository.Save("book", new CatalogDocument { Id = "book", Name = "Book" });
        var plugin = new LoansPlugin();
        plugin.InitializeCatalogRepository(new TestProvider(repository));

        await plugin.OnEntriesImportedAsync([Entry]);

        var data = plugin.GetAuxiliaryData();
        Assert.Contains(data, group => group.Category == "loans" && group.AffectedCount == 1 &&
            group.Sample == "Alice");
        Assert.Contains(data, group => group.Category == "borrowers" && group.AffectedCount == 1);
    }

    [Fact]
    public async Task DisabledPluginLeavesImportedPropertiesPlainAndContributesNoSurfaces()
    {
        using var workspace = new TestWorkspace();
        var catalogPath = Path.Combine(workspace.Root, "catalog");
        using (var database = OpenDatabase(workspace.Root))
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
            Assert.Empty(features.GetEntryDetailsTabs(Entry));
            Assert.False(await features.ExecuteEntryCommandAsync("skycd.loans.lend", Entry));

            Assert.Equal("Alice", repository.Get("book")!.Properties["Borrower"]);
            Assert.Equal("2024-03-01", repository.Get("book")!.Properties["LoanDate"]);
        }

        using var reopened = OpenDatabase(workspace.Root);
        var loans = CatalogLoanService.Open(OpenCatalog(reopened));
        loans.CatalogPath = catalogPath;
        Assert.Empty(loans.GetHistory("book"));
        Assert.Empty(loans.GetBorrowers());
    }

    [Fact]
    public async Task EnabledPluginCreatesLoansAndRestoresSurfacesAfterReEnable()
    {
        using var workspace = new TestWorkspace();
        using var database = OpenDatabase(workspace.Root);
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
        Assert.Equal(4, features.GetEntryCommands().Count);

        var loansTab = Assert.Single(features.GetEntryDetailsTabs(Entry));
        Assert.Equal("skycd.loans.details", loansTab.Key);
        Assert.EndsWith("Loans", loansTab.Title, StringComparison.Ordinal);

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

        EntryDetailsTabRequestEventArgs? tabRequest = null;
        features.DetailsTabRequested += (_, args) => tabRequest = args;

        Assert.False(await features.ExecuteEntryCommandAsync("skycd.loans.return", Entry));
        Assert.Null(tabRequest);
        Assert.False(await features.ExecuteEntryCommandAsync("skycd.loans.history", Entry));
        Assert.NotNull(tabRequest);
        Assert.Equal("book", tabRequest.EntryId);
        Assert.Equal("skycd.loans.details", tabRequest.TabKey);

        var service = CatalogLoanService.Open(repository);
        service.CatalogPath = provider.CatalogPath!;
        var active = Assert.Single(service.GetHistory("book"));
        var media = Entry with { Kind = CatalogEntryKind.Media };
        Assert.False(await plugin.ReturnSelectedAsync(service, [media], () => Task.FromResult(false)));
        Assert.True(Assert.Single(service.GetHistory("book")).IsActive);

        service.Edit("book", active.Id, active.BorrowerId, new DateOnly(2024, 3, 2), null, "Updated");
        var updated = Assert.Single(service.GetHistory("book"));
        Assert.Equal(active.Id, updated.Id);
        Assert.Equal("2024-03-02", updated.LoanDate);
        Assert.Equal("Updated", updated.Note);
        Assert.True(await plugin.ReturnSelectedAsync(service, [media], () => Task.FromResult(true)));
        Assert.Empty(features.GetDecorations([Entry]));
        Assert.Null(column.SelectValue(Entry));

        var history = Assert.Single(service.GetHistory("book"));
        Assert.False(history.IsActive);
    }

    [Theory]
    [InlineData(CatalogEntryKind.File, false)]
    [InlineData(CatalogEntryKind.Folder, false)]
    [InlineData(CatalogEntryKind.Other, false)]
    [InlineData(CatalogEntryKind.Media, true)]
    [InlineData(CatalogEntryKind.Volume, true)]
    public void LendingToolbarCommandsRequireEverySelectedEntryToBeLendable(CatalogEntryKind kind, bool expected)
    {
        var manager = new CatalogFeatureExtensionManager([new LoansPlugin()]);
        var toolbar = Assert.Single(manager.GetToolbars());
        Assert.Equal(3, toolbar.Buttons.Count);
        foreach (var button in toolbar.Buttons)
        {
            var command = Assert.Single(manager.GetEntryCommands(), item => item.CommandId == button.CommandId);
            var selection = new[] { Entry with { Kind = CatalogEntryKind.Media }, Entry with { Kind = kind } };
            Assert.Equal(expected, CatalogFeatureExtensionManager.CanExecute(command, selection));
            Assert.False(CatalogFeatureExtensionManager.CanExecute(command, []));
        }
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
}
