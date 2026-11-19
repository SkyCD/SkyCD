using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Presentation.Lending;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public sealed partial class LoanDialogViewModel : ObservableObject
{
    private readonly CatalogLoanService service;
    private readonly string entryId;
    private readonly Action onChanged;

    public LoanDialogViewModel(CatalogLoanService service, string entryId, Action onChanged)
    {
        this.service = service;
        this.entryId = entryId;
        this.onChanged = onChanged;
        Reload();
    }

    [ObservableProperty] private IReadOnlyList<BorrowerDocument> borrowers = [];
    [ObservableProperty] private IReadOnlyList<LoanHistoryRow> history = [];
    [ObservableProperty] private BorrowerDocument? selectedBorrower;
    [ObservableProperty] private LoanHistoryRow? selectedLoan;
    [ObservableProperty] private string contactName = string.Empty;
    [ObservableProperty] private string loanDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string dueDate = string.Empty;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string error = string.Empty;

    [RelayCommand]
    private void AddBorrower() => Apply(() => SelectedBorrower = service.AddBorrower(ContactName));

    [RelayCommand]
    private void RenameBorrower() => Apply(() =>
        service.RenameBorrower(SelectedBorrower?.Id ?? throw new LoanEditException(), ContactName));

    [RelayCommand]
    private void MarkLent() => Apply(() =>
    {
        if (SelectedBorrower is null || !TryDate(LoanDate, out var lent))
        {
            throw new LoanEditException();
        }

        DateOnly? due = null;
        if (!string.IsNullOrWhiteSpace(DueDate))
        {
            due = TryDate(DueDate, out var parsed) ? parsed : throw new LoanEditException();
        }

        service.Lend(entryId, SelectedBorrower.Id, lent, due, Note);
        SelectedLoan = null;
    });

    [RelayCommand]
    private void MarkReturned() => Apply(() =>
    {
        if (SelectedLoan is not { IsActive: true } loan)
        {
            throw new LoanEditException();
        }

        service.Return(entryId, loan.Id, DateOnly.FromDateTime(DateTime.Today));
    });

    private void Apply(Action action)
    {
        Error = string.Empty;
        try
        {
            action();
            Reload();
            onChanged();
        }
        catch (LoanEditException)
        {
            Error = LocalizationService.Current.Get(UiKeys.LoanInvalidEdit);
        }
    }

    private void Reload()
    {
        var borrowerId = SelectedBorrower?.Id;
        Borrowers = service.GetBorrowers();
        SelectedBorrower = Borrowers.FirstOrDefault(contact => contact.Id == borrowerId);
        var names = Borrowers.ToDictionary(contact => contact.Id, contact => contact.Name, StringComparer.Ordinal);
        var selectedId = SelectedLoan?.Id;
        History = service.GetHistory(entryId).Select(loan => new LoanHistoryRow(
            loan.Id, Describe(loan, names.GetValueOrDefault(loan.BorrowerId, string.Empty)), loan.IsActive)).ToArray();
        SelectedLoan = History.FirstOrDefault(loan => loan.Id == selectedId)
            ?? History.FirstOrDefault(loan => loan.IsActive);
    }

    private static string Describe(LoanDocument loan, string borrower)
    {
        var localization = LocalizationService.Current;
        var detail = loan.IsActive
            ? localization.Get(loan.IsOverdue(DateOnly.FromDateTime(DateTime.Today)) ? UiKeys.OverdueLoans : UiKeys.ActiveLoans)
            : localization.Format(UiKeys.LoanReturnedOn, loan.ReturnDate!);

        return string.Join(Environment.NewLine, new[]
        {
            borrower,
            detail,
            loan.LoanDate is { } loanDate ? localization.Format(UiKeys.LoanDateValue, loanDate) : null,
            loan.DueDate is { } dueDate ? localization.Format(UiKeys.LoanDueValue, dueDate) : null,
            loan.Note
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static bool TryDate(string? input, out DateOnly date) =>
        DateOnly.TryParseExact(input?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
