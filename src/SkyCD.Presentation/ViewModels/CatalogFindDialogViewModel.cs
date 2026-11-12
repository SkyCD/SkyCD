using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class CatalogFindDialogViewModel(IReadOnlyList<CatalogDocument> documents, Action<CatalogFindResult> navigate)
    : ObservableObject, IDisposable
{
    private CancellationTokenSource? searchCancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string query = string.Empty;

    [ObservableProperty] private IReadOnlyList<CatalogFindResult> results = [];
    [ObservableProperty] private CatalogFindResult? selectedResult;
    [ObservableProperty] private bool isSearching;
    [ObservableProperty] private string statusText = LocalizationService.Current.Get(TextKeys.FindEmptyQuery);

    private bool CanSearch() => !string.IsNullOrWhiteSpace(Query) && !IsSearching;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        searchCancellation = new CancellationTokenSource();
        var cancellationToken = searchCancellation.Token;
        IsSearching = true;
        StatusText = LocalizationService.Current.Get(TextKeys.FindSearching);
        try
        {
            var matches = await Task.Run(() => CatalogFindService.Search(documents, Query), cancellationToken);
            Results = matches;
            SelectedResult = matches.Count > 0 ? matches[0] : null;
            StatusText = matches.Count == 0
                ? LocalizationService.Current.Get(TextKeys.FindNoMatches)
                : LocalizationService.Current.Format(TextKeys.FindMatchCount, matches.Count);
        }
        catch (OperationCanceledException) { }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private void NavigateToResult(CatalogFindResult? result)
    {
        if (result is not null) navigate(result);
    }

    partial void OnQueryChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) return;
        Results = [];
        SelectedResult = null;
        StatusText = LocalizationService.Current.Get(TextKeys.FindEmptyQuery);
    }

    partial void OnIsSearchingChanged(bool value) => SearchCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        searchCancellation?.Cancel();
        searchCancellation?.Dispose();
        GC.SuppressFinalize(this);
    }
}
