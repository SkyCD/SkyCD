using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private CancellationTokenSource? quickFindCancellation;

    [ObservableProperty]
    private bool isQuickFilterOpen;

    [ObservableProperty]
    private string quickFindQuery = string.Empty;

    [ObservableProperty]
    private bool searchWholeCatalog;

    [ObservableProperty]
    private IReadOnlyList<CatalogFindResult> quickFindResults = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenQuickFindResultCommand))]
    private CatalogFindResult? selectedQuickFindResult;

    [ObservableProperty]
    private string quickFindStatus = string.Empty;

    private IReadOnlyList<CatalogDocument> filteredBrowserItems = [];

    public bool ShowQuickFindResults => SearchWholeCatalog && !string.IsNullOrWhiteSpace(QuickFindQuery);
    public bool ShowBrowserItems => !ShowQuickFindResults;
    public bool ShowBrowserContentsNotice => ShowContentsNotScannedNotice && string.IsNullOrWhiteSpace(QuickFindQuery);
    public IReadOnlyList<CatalogDocument> VisibleBrowserItems =>
        string.IsNullOrWhiteSpace(QuickFindQuery) || SearchWholeCatalog ? BrowserItems : filteredBrowserItems;

    partial void OnQuickFindQueryChanged(string value) => RefreshQuickFind();
    partial void OnSearchWholeCatalogChanged(bool value) => RefreshQuickFind();
    partial void OnShowContentsNotScannedNoticeChanged(bool value) =>
        OnPropertyChanged(nameof(ShowBrowserContentsNotice));

    [RelayCommand]
    private void CloseQuickFilter()
    {
        IsQuickFilterOpen = false;
        QuickFindQuery = string.Empty;
        SearchWholeCatalog = false;
    }

    private bool CanOpenQuickFindResult() => SelectedQuickFindResult is not null;

    [RelayCommand(CanExecute = nameof(CanOpenQuickFindResult))]
    private void OpenQuickFindResult()
    {
        if (SelectedQuickFindResult is not { } result) return;

        CloseQuickFilter();
        NavigateToFindResult(result);
    }

    private void RefreshQuickFind()
    {
        quickFindCancellation?.Cancel();
        QuickFindResults = [];
        SelectedQuickFindResult = null;
        filteredBrowserItems = [];
        var query = QuickFindQuery.Trim();
        if (ShowQuickFindResults) SelectedBrowserItem = null;

        OnPropertyChanged(nameof(ShowQuickFindResults));
        OnPropertyChanged(nameof(ShowBrowserItems));
        OnPropertyChanged(nameof(VisibleBrowserItems));
        OnPropertyChanged(nameof(ShowBrowserContentsNotice));

        if (query.Length == 0)
        {
            QuickFindStatus = LocalizationService.Current.Get(SearchWholeCatalog ? TextKeys.FindEmptyQuery : TextKeys.QuickFindEmptyQuery);
            return;
        }

        quickFindCancellation = new CancellationTokenSource();
        _ = UpdateQuickFindAsync(query, SearchWholeCatalog, quickFindCancellation);
    }

    private async Task UpdateQuickFindAsync(string query, bool wholeCatalog, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        QuickFindStatus = LocalizationService.Current.Get(TextKeys.FindSearching);
        try
        {
            if (wholeCatalog)
            {
                await Task.Delay(150, token);
                var repository = catalogRepository;
                var memoryEntries = repository is null ? GetAllCatalogEntries() : null;
                var fieldProvider = searchFieldProvider;
                var matches = await Task.Run(() => CatalogFindService.Search(
                    repository?.GetAll() ?? memoryEntries ?? [], query, fieldProvider, token), token);
                if (token.IsCancellationRequested) return;

                QuickFindResults = matches;
                SelectedQuickFindResult = matches.FirstOrDefault();
                SetQuickFindCount(matches.Count);
            }
            else
            {
                var items = BrowserItems.ToArray();
                var matches = await Task.Run(() => items.Where(item =>
                    item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray(), token);
                if (token.IsCancellationRequested) return;

                filteredBrowserItems = matches;
                OnPropertyChanged(nameof(VisibleBrowserItems));
                SetQuickFindCount(matches.Length);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!token.IsCancellationRequested)
            {
                QuickFindStatus = LocalizationService.Current.Format(TextKeys.AdvancedFindFailed, exception.Message);
            }
        }
        finally
        {
            if (ReferenceEquals(quickFindCancellation, cancellation)) quickFindCancellation = null;
            cancellation.Dispose();
        }
    }

    private void SetQuickFindCount(int count) => QuickFindStatus = count == 0
        ? LocalizationService.Current.Get(TextKeys.FindNoMatches)
        : LocalizationService.Current.Format(TextKeys.FindMatchCount, count);
}
