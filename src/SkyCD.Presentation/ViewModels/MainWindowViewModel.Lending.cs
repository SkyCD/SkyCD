using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Plugin.Feature.Loans;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public partial class MainWindowViewModel
{
    private readonly CatalogLoanService? loanService;
    private string? loanFilter;

    public event EventHandler<LoanDialogRequestedEventArgs>? LoansRequested;

    public event EventHandler<ContactsDialogRequestedEventArgs>? ContactsRequested;

    private string? LendingEntryId => ActivePane == ActivePane.Tree
        ? SelectedTreeNode?.Key
        : SelectedBrowserItem?.Id ?? SelectedTreeNode?.Key;

    [RelayCommand]
    private void OpenContactsDialog()
    {
        if (loanService is null || catalogRepository is null)
        {
            return;
        }

        var dialog = new ContactsDialogViewModel(loanService, RefreshLending);
        ContactsRequested?.Invoke(this, new ContactsDialogRequestedEventArgs { Dialog = dialog });
    }

    [RelayCommand]
    private void OpenLoans(string? action)
    {
        if (LendingEntryId is { } id && CreateLoanEditor(id) is { } editor)
        {
            if (action == "return" && editor.SelectedLoan is { IsActive: true })
            {
                editor.MarkReturnedCommand.Execute(null);
                if (editor.Error.Length > 0)
                {
                    StatusText = editor.Error;
                }
                return;
            }

            LoansRequested?.Invoke(this, new LoanDialogRequestedEventArgs(editor));
        }
    }

    private LoanDialogViewModel? CreateLoanEditor(string entryId) =>
        loanService is not null && catalogRepository?.Get(entryId) is not null
            ? new LoanDialogViewModel(loanService, entryId, RefreshLending)
            : null;

    private void RefreshLending()
    {
        RefreshTreeNodes(SelectedTreeNode?.Key);
        RefreshBrowserItemsForSelection();
        RefreshCollectionsMenuItems();
    }

    partial void OnCurrentCatalogPathChanged(string? value)
    {
        if (loanService is not null)
        {
            loanService.CatalogPath = value ?? string.Empty;
            RefreshLending();
        }
    }

    private IReadOnlyList<CatalogDocument> ProjectLending(IReadOnlyList<CatalogDocument> entries)
    {
        if (loanService is not null)
        {
            var active = loanService.GetActiveLoans(DateOnly.FromDateTime(DateTime.Today));
            foreach (var entry in entries)
            {
                entry.SetLendingState(active.TryGetValue(entry.Id, out var state) ? state : null);
            }
        }
        return entries;
    }

    private bool MatchesLendingFilter(CatalogDocument entry) => loanFilter switch
    {
        "active" => entry.IsLent,
        "overdue" => entry.IsLoanOverdue,
        _ => true
    };

    [RelayCommand]
    private void SetLendingFilter(string? mode)
    {
        loanFilter = mode;
        RefreshBrowserItemsForSelection();
        StatusText = LocalizationService.Current.Get(mode switch
        {
            "active" => UiKeys.ActiveLoans,
            "overdue" => UiKeys.OverdueLoans,
            _ => UiKeys.AllLoanStates
        });
    }

    private MainMenuItemViewModel BuildLoansMenu() => new()
    {
        Header = LocalizationService.Current.Get(UiKeys.Loans),
        Items =
        [
            new() { Header = LocalizationService.Current.Get(UiKeys.MarkLent), Command = OpenLoansCommand, CommandParameter = "lend" },
            new() { Header = LocalizationService.Current.Get(UiKeys.MarkReturned), Command = OpenLoansCommand, CommandParameter = "return" },
            new() { Header = LocalizationService.Current.Get(UiKeys.LoanHistory), Command = OpenLoansCommand },
            new() { Header = LocalizationService.Current.Get(UiKeys.ActiveLoans), Command = SetLendingFilterCommand, CommandParameter = "active" },
            new() { Header = LocalizationService.Current.Get(UiKeys.OverdueLoans), Command = SetLendingFilterCommand, CommandParameter = "overdue" },
            new() { Header = LocalizationService.Current.Get(UiKeys.AllLoanStates), Command = SetLendingFilterCommand },
            new() { Header = "-" },
            new() { Header = LocalizationService.Current.Get(UiKeys.ManageContacts), Command = OpenContactsDialogCommand }
        ]
    };
}
