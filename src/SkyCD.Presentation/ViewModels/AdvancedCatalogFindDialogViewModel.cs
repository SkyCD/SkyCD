using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.Collections;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>Sort order for advanced-find results. Comparisons use stored values, never display strings.</summary>
public enum AdvancedFindSortKey
{
    FullPath,
    Name,
    Size,
    Type,
    Status
}

/// <summary>
/// Reusable advanced query editor and results view over <see cref="AdvancedCatalogQuery"/>.
/// Typed filter rows compose into one AND/OR group, validation blocks incomplete rows,
/// and searches run off the UI thread with superseded passes cancelled and discarded.
/// </summary>
public sealed partial class AdvancedCatalogFindDialogViewModel(
    IReadOnlyList<CatalogDocument>? documents = null,
    Action<CatalogFindResult>? navigate = null,
    AdvancedCatalogSearchService.MetadataLookup? metadataLookup = null,
    IReadOnlyList<string>? metadataKeys = null,
    IReadOnlyList<string>? customPropertyKeys = null,
    IReadOnlyList<string>? statusValues = null,
    VirtualCollectionService? savedSearchService = null,
    Action? onSavedSearchChanged = null)
    : ObservableObject, IDisposable
{
    public const int PageSize = 100;

    private readonly IReadOnlyList<CatalogDocument> documents = documents ?? [];
    private readonly Action<CatalogFindResult>? navigate = navigate;
    private readonly AdvancedCatalogSearchService.MetadataLookup? metadataLookup = metadataLookup;

    private CancellationTokenSource? searchCancellation;
    private int searchGeneration;
    private IReadOnlyList<CatalogFindResult> allMatches = [];
    private bool disposed;
    private CatalogQueryScope loadedScope = new();

    public IReadOnlyList<VirtualCollectionDocument> SavedSearches => savedSearchService?.GetCollections()
        .Where(collection => collection.Kind == VirtualCollectionKind.Dynamic).ToArray() ?? [];

    public bool CanSaveSearch => savedSearchService is not null;

    [ObservableProperty]
    private VirtualCollectionDocument? selectedSavedSearch;

    [ObservableProperty]
    private string savedSearchName = string.Empty;

    [RelayCommand]
    private void LoadSavedSearch()
    {
        if (savedSearchService is null || SelectedSavedSearch is null)
        {
            return;
        }

        try
        {
            var query = savedSearchService.GetDynamicQuery(SelectedSavedSearch.Id);
            if (query.Root.Groups.Count != 0 || query.Scope.Types?.Count > 1)
            {
                throw new SavedSearchException();
            }

            Filters.Clear();
            foreach (var predicate in query.Root.Predicates)
            {
                Filters.Add(new AdvancedCatalogFilterRowViewModel
                {
                    SelectedField = predicate.Field,
                    SelectedOperator = predicate.Operator,
                    Key = predicate.Key ?? string.Empty,
                    ValueText = predicate.Operator == AdvancedCatalogOperator.OneOf
                        ? string.Join(';', predicate.Values ?? [])
                        : Convert.ToString(predicate.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                });
            }

            loadedScope = query.Scope;
            SelectedScopeType = query.Scope.Types is { Count: > 0 } ? query.Scope.Types[0] : null;
            SelectedCombination = query.Root.Combination;
            SelectedSortKey = query.SortKey;
            SortDescending = query.SortDescending;
            SelectedDuplicateFilter = query.DuplicateFilter;
            SelectedHashAlgorithm = query.HashAlgorithm;
            SavedSearchName = SelectedSavedSearch.Name;
            ClearResults();
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand]
    private void SaveSearch() => PersistSearch(false);

    [RelayCommand]
    private void UpdateSavedSearch() => PersistSearch(true);

    private void PersistSearch(bool update)
    {
        if (savedSearchService is null)
        {
            return;
        }

        if (!TryBuildQuery(out var query, out var error))
        {
            StatusText = error ?? string.Empty;
            return;
        }

        try
        {
            if (update && SelectedSavedSearch is { } selected)
            {
                savedSearchService.UpdateDynamicCollection(selected.Id, query);
            }
            else if (!update)
            {
                savedSearchService.CreateDynamicCollection(SavedSearchName, query);
            }

            OnPropertyChanged(nameof(SavedSearches));
            onSavedSearchChanged?.Invoke();
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
        }
    }

    public ObservableCollection<AdvancedCatalogFilterRowViewModel> Filters { get; } = [];

    public IReadOnlyList<AdvancedCatalogField> AvailableFields { get; } =
        Enum.GetValues<AdvancedCatalogField>();

    public IReadOnlyList<AdvancedCatalogOperator> AvailableOperators { get; } =
        Enum.GetValues<AdvancedCatalogOperator>();

    public IReadOnlyList<CatalogQueryCombination> AvailableCombinations { get; } =
        Enum.GetValues<CatalogQueryCombination>();

    public IReadOnlyList<CatalogDocumentType?> AvailableScopeTypes { get; } =
        [null, .. Enum.GetValues<CatalogDocumentType>().Cast<CatalogDocumentType?>()];

    public IReadOnlyList<AdvancedFindSortKey> AvailableSortKeys { get; } =
        Enum.GetValues<AdvancedFindSortKey>();

    public IReadOnlyList<string> AvailableMetadataKeys { get; } = metadataKeys ?? [];

    public IReadOnlyList<string> AvailablePropertyKeys { get; } = customPropertyKeys ?? [];

    public IReadOnlyList<string> AvailableStatuses { get; } = statusValues ?? [];

    public bool HasMetadataLookup => metadataLookup is not null;

    [ObservableProperty]
    private CatalogQueryCombination selectedCombination = CatalogQueryCombination.All;

    [ObservableProperty]
    private CatalogDocumentType? selectedScopeType;

    [ObservableProperty]
    private AdvancedFindSortKey selectedSortKey = AdvancedFindSortKey.FullPath;

    [ObservableProperty]
    private bool sortDescending;

    [ObservableProperty]
    private DuplicateFilterMode selectedDuplicateFilter = DuplicateFilterMode.None;

    [ObservableProperty]
    private ComparisonAlgorithm selectedHashAlgorithm = ComparisonAlgorithm.None;

    public IReadOnlyList<DuplicateFilterMode> AvailableDuplicateFilters { get; } =
        Enum.GetValues<DuplicateFilterMode>();

    public IReadOnlyList<ComparisonAlgorithm> AvailableHashAlgorithms { get; } =
        Enum.GetValues<ComparisonAlgorithm>();

    [ObservableProperty]
    private IReadOnlyList<CatalogFindResult> results = [];

    [ObservableProperty]
    private CatalogFindResult? selectedResult;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private string statusText = LocalizationService.Current.Get(TextKeys.AdvancedFindHint);

    [ObservableProperty]
    private int pageIndex;

    [ObservableProperty]
    private int pageCount = 1;

    [ObservableProperty]
    private int totalMatches;

    [ObservableProperty]
    private bool resultsTruncated;

    [ObservableProperty]
    private string pageLabel = string.Empty;

    public AdvancedCatalogFindDialogViewModel()
        : this(null, null, null, null, null, null)
    {
    }

    public bool CanSearch() => !IsSearching;

    public bool CanCancelSearch() => IsSearching;

    public bool CanPage() => !IsSearching && PageCount > 1;

    public bool CanPreviousPage() => CanPage() && PageIndex > 0;

    public bool CanNextPage() => CanPage() && PageIndex < PageCount - 1;

    public bool CanNavigateToResult(CatalogFindResult? result) => navigate is not null && result is not null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task SearchAsync()
    {
        if (disposed)
        {
            return;
        }

        if (!TryBuildQuery(out var query, out var invalid))
        {
            StatusText = invalid ?? LocalizationService.Current.Get(TextKeys.AdvancedFindHint);
            return;
        }

        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        var cancellationToken = searchCancellation.Token;
        var generation = ++searchGeneration;
        IsSearching = true;
        SearchCommand.NotifyCanExecuteChanged();
        CancelSearchCommand.NotifyCanExecuteChanged();
        StatusText = LocalizationService.Current.Get(TextKeys.FindSearching);
        try
        {
            var snapshot = documents;
            var lookup = metadataLookup;
            var matches = await Task.Run(() =>
            {
                var index = new CatalogSearchIndex();
                index.Rebuild(snapshot);
                return AdvancedCatalogSearchService.Search(index, query, lookup, cancellationToken);
            }, cancellationToken);

            if (disposed || generation != searchGeneration)
            {
                return;
            }

            ApplyMatches(matches);
        }
        catch (OperationCanceledException)
        {
            if (!disposed && generation == searchGeneration)
            {
                StatusText = LocalizationService.Current.Get(TextKeys.AdvancedFindCancelled);
            }
        }
        catch (Exception exception)
        {
            if (!disposed && generation == searchGeneration)
            {
                StatusText = LocalizationService.Current.Format(TextKeys.AdvancedFindFailed, exception.Message);
            }
        }
        finally
        {
            if (!disposed && generation == searchGeneration)
            {
                IsSearching = false;
                SearchCommand.NotifyCanExecuteChanged();
                CancelSearchCommand.NotifyCanExecuteChanged();
                RefreshPagingCommands();
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelSearch))]
    private void CancelSearch()
    {
        searchCancellation?.Cancel();
    }

    [RelayCommand]
    private void AddFilter() => Filters.Add(new AdvancedCatalogFilterRowViewModel());

    [RelayCommand]
    private void RemoveFilter(AdvancedCatalogFilterRowViewModel? row)
    {
        if (row is not null)
        {
            Filters.Remove(row);
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filters.Clear();
        ClearResults();
    }

    [RelayCommand]
    private void ResetFilters()
    {
        loadedScope = new();
        Filters.Clear();
        Filters.Add(new AdvancedCatalogFilterRowViewModel());
        SelectedCombination = CatalogQueryCombination.All;
        SelectedScopeType = null;
        SelectedDuplicateFilter = DuplicateFilterMode.None;
        SelectedHashAlgorithm = ComparisonAlgorithm.None;
        ClearResults();
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private void PreviousPage()
    {
        if (PageIndex > 0)
        {
            PageIndex--;
            ApplyPage();
        }
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private void NextPage()
    {
        if (PageIndex < PageCount - 1)
        {
            PageIndex++;
            ApplyPage();
        }
    }

    [RelayCommand(CanExecute = nameof(CanNavigateToResult))]
    private void NavigateToResult(CatalogFindResult? result)
    {
        if (result is not null)
        {
            navigate?.Invoke(result);
        }
    }

    partial void OnSelectedCombinationChanged(CatalogQueryCombination value) => ClearResults();

    partial void OnSelectedScopeTypeChanged(CatalogDocumentType? value) => ClearResults();

    partial void OnSelectedSortKeyChanged(AdvancedFindSortKey value) => ReapplySort();

    partial void OnSortDescendingChanged(bool value) => ReapplySort();

    partial void OnIsSearchingChanged(bool value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        CancelSearchCommand.NotifyCanExecuteChanged();
        RefreshPagingCommands();
    }

    partial void OnPageIndexChanged(int value) => RefreshPagingCommands();

    partial void OnPageCountChanged(int value) => RefreshPagingCommands();

    partial void OnSelectedResultChanged(CatalogFindResult? value) =>
        NavigateToResultCommand.NotifyCanExecuteChanged();

    private bool TryBuildQuery(out AdvancedCatalogQuery query, out string? invalid)
    {
        query = null!;
        invalid = null;
        var predicates = new List<AdvancedCatalogPredicate>(Filters.Count);
        for (var index = 0; index < Filters.Count; index++)
        {
            if (!Filters[index].TryBuildPredicate(out var predicate, out var error) || predicate is null)
            {
                invalid = LocalizationService.Current.Format(
                    TextKeys.AdvancedFindInvalid, index + 1, error ?? string.Empty);
                return false;
            }

            predicates.Add(predicate);
        }

        IReadOnlyList<CatalogDocumentType>? types = SelectedScopeType is { } scope
            ? [scope]
            : null;
        query = new AdvancedCatalogQuery
        {
            Scope = loadedScope with { Types = types },
            SortKey = SelectedSortKey,
            SortDescending = SortDescending,
            Root = new CatalogQueryGroup
            {
                Combination = SelectedCombination,
                Predicates = predicates
            },
            Take = AdvancedCatalogSearchService.MaxTake,
            DuplicateFilter = SelectedDuplicateFilter,
            HashAlgorithm = SelectedHashAlgorithm
        };
        return true;
    }

    private void ApplyMatches(IReadOnlyList<CatalogFindResult> matches)
    {
        allMatches = matches;
        ResultsTruncated = matches.Count >= AdvancedCatalogSearchService.MaxTake;
        TotalMatches = matches.Count;
        PageIndex = 0;
        ReapplySort();
    }

    private void ReapplySort()
    {
        var sorted = SortMatches(allMatches, SelectedSortKey, SortDescending);
        allMatches = sorted;
        PageCount = Math.Max(1, (sorted.Count + PageSize - 1) / PageSize);
        PageIndex = Math.Min(PageIndex, PageCount - 1);
        ApplyPage();
    }

    internal static IReadOnlyList<CatalogFindResult> SortMatches(
        IReadOnlyList<CatalogFindResult> matches,
        AdvancedFindSortKey sortKey,
        bool descending)
    {
        if (matches.Count == 0)
        {
            return matches;
        }

        IOrderedEnumerable<CatalogFindResult> ordered = sortKey switch
        {
            AdvancedFindSortKey.Name => matches
                .OrderBy(static match => match.Document.Name, StringComparer.OrdinalIgnoreCase),
            AdvancedFindSortKey.Size => matches
                .OrderBy(static match => match.Document.Size),
            AdvancedFindSortKey.Type => matches
                .OrderBy(static match => match.Document.Type.ToString(), StringComparer.Ordinal),
            AdvancedFindSortKey.Status => matches
                .OrderBy(static match => match.Document.Status is null, Comparer<bool>.Default)
                .ThenBy(static match => match.Document.Status, StringComparer.OrdinalIgnoreCase),
            _ => matches
                .OrderBy(static match => match.FullPath, StringComparer.OrdinalIgnoreCase)
        };

        ordered = ordered
            .ThenBy(static match => match.FullPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static match => match.Document.Id, StringComparer.Ordinal);
        var sorted = ordered.ToArray();
        if (descending)
        {
            Array.Reverse(sorted);
        }

        return sorted;
    }

    private void ApplyPage()
    {
        var matches = allMatches;
        var start = PageIndex * PageSize;
        Results = start >= matches.Count
            ? []
            : matches.Skip(start).Take(PageSize).ToArray();
        SelectedResult = Results.Count > 0 ? Results[0] : null;
        PageLabel = $"Page {PageIndex + 1} of {PageCount}";
        UpdatePageStatus();
        RefreshPagingCommands();
    }

    private void UpdatePageStatus()
    {
        if (allMatches.Count == 0)
        {
            StatusText = LocalizationService.Current.Get(TextKeys.FindNoMatches);
            return;
        }

        var first = PageIndex * PageSize + 1;
        var last = Math.Min(allMatches.Count, (PageIndex + 1) * PageSize);
        var status = LocalizationService.Current.Format(
            TextKeys.AdvancedFindPageStatus, first, last, TotalMatches);
        if (ResultsTruncated)
        {
            status += " " + LocalizationService.Current.Format(
                TextKeys.AdvancedFindTruncated, AdvancedCatalogSearchService.MaxTake);
        }

        StatusText = status;
    }

    private void ClearResults()
    {
        allMatches = [];
        Results = [];
        SelectedResult = null;
        TotalMatches = 0;
        ResultsTruncated = false;
        PageIndex = 0;
        PageCount = 1;
        PageLabel = string.Empty;
        StatusText = LocalizationService.Current.Get(TextKeys.AdvancedFindHint);
        RefreshPagingCommands();
    }

    private void RefreshPagingCommands()
    {
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        NavigateToResultCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = null;
        GC.SuppressFinalize(this);
    }
}
