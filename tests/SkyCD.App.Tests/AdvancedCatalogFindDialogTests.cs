using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class AdvancedCatalogFindDialogTests
{
    private static CatalogDocument Doc(
        string id,
        string? parentId = null,
        string? name = null,
        CatalogDocumentType type = CatalogDocumentType.File,
        long size = 0,
        string? status = null)
    {
        return new CatalogDocument
        {
            Id = id,
            Name = name ?? id,
            ParentId = parentId,
            Type = type,
            Size = size,
            ChildrenCount = 0,
            Status = status
        };
    }

    private static AdvancedCatalogFindDialogViewModel Create(
        IReadOnlyList<CatalogDocument> documents,
        Action<CatalogFindResult>? navigate = null,
        AdvancedCatalogSearchService.MetadataLookup? lookup = null,
        IReadOnlyList<string>? metadataKeys = null,
        IReadOnlyList<string>? propertyKeys = null)
    {
        return new AdvancedCatalogFindDialogViewModel(
            documents, navigate, lookup, metadataKeys, propertyKeys, null);
    }

    private static AdvancedCatalogFilterRowViewModel Row(
        AdvancedCatalogField field,
        AdvancedCatalogOperator op,
        string value,
        string key = "")
    {
        return new AdvancedCatalogFilterRowViewModel
        {
            SelectedField = field,
            SelectedOperator = op,
            Key = key,
            ValueText = value
        };
    }

    [Fact]
    public async Task AndFilters_ReturnOnlyEntriesMatchingAll()
    {
        var documents = new[]
        {
            Doc("a", name: "Sunset.jpg", size: 2_500_000),
            Doc("b", name: "Sunset.txt", size: 512),
            Doc("c", name: "Notes.jpg", size: 100)
        };
        using var vm = Create(documents);
        vm.Filters.Add(Row(AdvancedCatalogField.Name, AdvancedCatalogOperator.Contains, "sunset"));
        vm.Filters.Add(Row(AdvancedCatalogField.Extension, AdvancedCatalogOperator.Equals, "jpg"));

        await vm.SearchAsync();

        Assert.Equal("a", vm.Results.Single().Document.Id);
        Assert.Equal(1, vm.TotalMatches);
    }

    [Fact]
    public async Task OrCombination_ReturnsUnion()
    {
        var documents = new[]
        {
            Doc("a", name: "Sunset.jpg"),
            Doc("b", name: "Notes.txt"),
            Doc("c", name: "Other.bin")
        };
        using var vm = Create(documents);
        vm.SelectedCombination = CatalogQueryCombination.Any;
        vm.Filters.Add(Row(AdvancedCatalogField.Extension, AdvancedCatalogOperator.Equals, "jpg"));
        vm.Filters.Add(Row(AdvancedCatalogField.Extension, AdvancedCatalogOperator.Equals, "txt"));

        await vm.SearchAsync();

        Assert.Equal(["a", "b"], vm.Results.Select(match => match.Document.Id).OrderBy(static id => id));
    }

    [Fact]
    public async Task NoMatches_ReportsEmptyResults()
    {
        using var vm = Create([Doc("a", name: "Sunset.jpg")]);
        vm.Filters.Add(Row(AdvancedCatalogField.Name, AdvancedCatalogOperator.Contains, "zzz"));

        await vm.SearchAsync();

        Assert.Empty(vm.Results);
        Assert.Equal(0, vm.TotalMatches);
        Assert.Equal("No matching catalog entries.", vm.StatusText);
    }

    [Fact]
    public async Task EmptyFilters_ListEverything()
    {
        using var vm = Create([Doc("a"), Doc("b")]);

        await vm.SearchAsync();

        Assert.Equal(2, vm.TotalMatches);
        Assert.Equal("Showing 1–2 of 2 matches.", vm.StatusText);
    }

    [Fact]
    public async Task ValidationError_BlocksSearch_AndRetrySucceeds()
    {
        using var vm = Create([Doc("a", name: "Sunset.jpg")]);
        var row = Row(AdvancedCatalogField.CustomProperty, AdvancedCatalogOperator.Contains, "x");
        vm.Filters.Add(row);

        await vm.SearchAsync();

        Assert.Empty(vm.Results);
        Assert.StartsWith("Filter 1:", vm.StatusText);

        row.Key = "Comment";
        await vm.SearchAsync();

        Assert.Empty(vm.Results);
        Assert.Equal("No matching catalog entries.", vm.StatusText);
    }

    [Fact]
    public async Task SupersededSearch_AppliesLatestResultsOnly()
    {
        using var gate = new ManualResetEventSlim(false);
        var calls = 0;
        AdvancedCatalogSearchService.MetadataLookup lookup = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1 && !gate.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Lookup gate timed out.");
            }

            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        };
        using var vm = Create([Doc("a", name: "Alpha"), Doc("b", name: "Beta")], lookup: lookup);
        var row = Row(AdvancedCatalogField.Metadata, AdvancedCatalogOperator.IsMissing, string.Empty, "Capture");
        vm.Filters.Add(row);

        var first = vm.SearchAsync();
        row.SelectedField = AdvancedCatalogField.Name;
        row.SelectedOperator = AdvancedCatalogOperator.Contains;
        row.ValueText = "zzz";
        var second = vm.SearchAsync();
        gate.Set();
        await Task.WhenAll(first, second);

        Assert.Empty(vm.Results);
        Assert.Equal(0, vm.TotalMatches);
        Assert.False(vm.IsSearching);
    }

    [Fact]
    public async Task CancelledSearch_KeepsPreviousResultsAndReportsCancelled()
    {
        using var gate = new ManualResetEventSlim(false);
        var calls = 0;
        AdvancedCatalogSearchService.MetadataLookup lookup = _ =>
        {
            if (Interlocked.Increment(ref calls) == 2 && !gate.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Lookup gate timed out.");
            }

            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        };
        using var vm = Create([Doc("a", name: "Alpha")], lookup: lookup);
        vm.Filters.Add(Row(AdvancedCatalogField.Metadata, AdvancedCatalogOperator.IsMissing, string.Empty, "Capture"));

        await vm.SearchAsync();
        Assert.Single(vm.Results);

        var blocked = vm.SearchAsync();
        Assert.True(vm.CancelSearchCommand.CanExecute(null));
        Assert.False(vm.SearchCommand.CanExecute(null));
        vm.CancelSearchCommand.Execute(null);
        gate.Set();
        await blocked;

        Assert.Single(vm.Results);
        Assert.False(vm.IsSearching);
        Assert.Equal("Search cancelled.", vm.StatusText);
    }

    [Fact]
    public async Task Paging_SplitsMatchesIntoHundredItemPages()
    {
        var documents = Enumerable.Range(0, 250)
            .Select(index => Doc($"file-{index:000}", name: $"File {index:000}"))
            .ToArray();
        using var vm = Create(documents);

        await vm.SearchAsync();

        Assert.Equal(250, vm.TotalMatches);
        Assert.Equal(3, vm.PageCount);
        Assert.Equal(100, vm.Results.Count);
        Assert.True(vm.NextPageCommand.CanExecute(null));
        Assert.False(vm.PreviousPageCommand.CanExecute(null));

        vm.NextPageCommand.Execute(null);

        Assert.Equal(1, vm.PageIndex);
        Assert.Equal(100, vm.Results.Count);
        Assert.True(vm.PreviousPageCommand.CanExecute(null));

        vm.NextPageCommand.Execute(null);

        Assert.Equal(2, vm.PageIndex);
        Assert.Equal(50, vm.Results.Count);
        Assert.False(vm.NextPageCommand.CanExecute(null));

        vm.PreviousPageCommand.Execute(null);

        Assert.Equal(1, vm.PageIndex);
    }

    [Fact]
    public async Task Sorting_OrdersBySelectedKey()
    {
        var documents = new[]
        {
            Doc("big", name: "Big", size: 300),
            Doc("small", name: "Small", size: 100),
            Doc("mid", name: "Mid", size: 200)
        };
        using var vm = Create(documents);

        await vm.SearchAsync();

        vm.SelectedSortKey = AdvancedFindSortKey.Size;

        Assert.Equal(["small", "mid", "big"], vm.Results.Select(match => match.Document.Id));

        vm.SortDescending = true;

        Assert.Equal(["big", "mid", "small"], vm.Results.Select(match => match.Document.Id));
    }

    [Fact]
    public async Task SubtreeScope_ChangesFromWholeCatalogToRootOnlyOrDescendants()
    {
        var documents = new[]
        {
            Doc("disc", type: CatalogDocumentType.Media),
            Doc("folder", "disc", type: CatalogDocumentType.Folder),
            Doc("nested", "folder"),
            Doc("outside", "disc")
        };
        using var vm = new AdvancedCatalogFindDialogViewModel(documents, selectedSubtreeId: "folder");

        await vm.SearchAsync();
        Assert.Equal(4, vm.TotalMatches);

        vm.SearchSubtree = true;
        vm.IncludeDescendants = false;
        await vm.SearchAsync();
        Assert.Equal("folder", Assert.Single(vm.Results).Document.Id);

        vm.IncludeDescendants = true;
        await vm.SearchAsync();
        Assert.Equal(["folder", "nested"], vm.Results.Select(match => match.Document.Id));
    }

    [Theory]
    [InlineData(1000, false)]
    [InlineData(1001, true)]
    public async Task ResultLimit_ReportsTheExactTotalAndOnlyPagesAvailableMatches(int count, bool limited)
    {
        using var vm = Create(Enumerable.Range(0, count).Select(index => Doc($"file-{index}")).ToArray());

        await vm.SearchAsync();

        Assert.Equal(count, vm.TotalMatches);
        Assert.Equal(limited, vm.ResultsTruncated);
        Assert.Equal(10, vm.PageCount);
    }

    [Fact]
    public async Task ScopeType_LimitsToEntryKind()
    {
        var documents = new[]
        {
            Doc("disc", name: "Disc", type: CatalogDocumentType.Media),
            Doc("folder", name: "Folder", type: CatalogDocumentType.Folder, parentId: "disc"),
            Doc("file", name: "File", type: CatalogDocumentType.File, parentId: "folder")
        };
        using var vm = Create(documents);
        vm.SelectedScopeType = CatalogDocumentType.Media;

        await vm.SearchAsync();

        Assert.Equal("disc", vm.Results.Single().Document.Id);
    }

    [Fact]
    public async Task CustomProperty_AndMetadataKeys_AreSearchable()
    {
        var tagged = Doc("a", name: "Tagged");
        tagged.Properties["Genre"] = "Soundtrack";
        var documents = new[] { tagged, Doc("b", name: "Plain") };
        AdvancedCatalogSearchService.MetadataLookup lookup = document =>
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Codec"] = string.Equals(document.Id, "a", StringComparison.Ordinal) ? "FLAC 16-bit" : null
            };
        using var vm = Create(
            documents,
            lookup: lookup,
            metadataKeys: ["Codec"],
            propertyKeys: ["Genre"]);

        Assert.Contains("Codec", vm.AvailableMetadataKeys);
        Assert.Contains("Genre", vm.AvailablePropertyKeys);

        vm.Filters.Add(Row(AdvancedCatalogField.CustomProperty, AdvancedCatalogOperator.Equals, "Soundtrack", "Genre"));
        await vm.SearchAsync();
        Assert.Equal("a", vm.Results.Single().Document.Id);

        vm.Filters.Clear();
        vm.Filters.Add(Row(AdvancedCatalogField.Metadata, AdvancedCatalogOperator.Contains, "FLAC", "Codec"));
        await vm.SearchAsync();
        Assert.Equal("a", vm.Results.Single().Document.Id);
    }

    [Fact]
    public async Task Navigation_InvokesCallbackWithDocument()
    {
        CatalogFindResult? navigated = null;
        using var vm = Create([Doc("a", name: "Alpha")], navigate: result => navigated = result);

        await vm.SearchAsync();
        vm.NavigateToResultCommand.Execute(vm.SelectedResult);

        Assert.Equal("a", navigated?.Document.Id);
    }

    [Fact]
    public void Clear_AndReset_RestoreEditorState()
    {
        using var vm = Create([Doc("a")]);
        vm.AddFilterCommand.Execute(null);
        vm.AddFilterCommand.Execute(null);
        vm.SelectedCombination = CatalogQueryCombination.Any;

        vm.ClearFiltersCommand.Execute(null);

        Assert.Empty(vm.Filters);
        Assert.Equal("Add filters and choose Search, or search with no filters to list everything.", vm.StatusText);

        vm.ResetFiltersCommand.Execute(null);

        Assert.Single(vm.Filters);
        Assert.Equal(CatalogQueryCombination.All, vm.SelectedCombination);
        Assert.Null(vm.SelectedScopeType);
    }
}
