using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Plugin.Feature.Loans.Exceptions;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.UI.Localization;

namespace SkyCD.Plugin.Feature.Loans.Views;

public sealed partial class LoanDialogViewModel : ObservableObject
{
    private readonly CatalogLoanService service;
    private readonly string entryId;
    private readonly Action onChanged;
    private readonly Func<Task>? openContactsAsync;
    private readonly LoanDocument? editLoan;

    public LoanDialogViewModel(CatalogLoanService service, string entryId, Action onChanged,
        Func<Task>? openContactsAsync = null, bool isPopup = false, LoanDocument? editLoan = null)
    {
        this.service = service;
        this.entryId = entryId;
        this.onChanged = onChanged;
        this.openContactsAsync = openContactsAsync;
        IsPopup = isPopup;
        this.editLoan = editLoan;
        Reload();
        if (editLoan is not null)
        {
            SelectedBorrower = Borrowers.FirstOrDefault(contact => contact.Id == editLoan.BorrowerId);
            LoanDate = EditDate(editLoan.LoanDate);
            DueDate = EditDate(editLoan.DueDate);
            Note = editLoan.Note ?? string.Empty;
        }
    }

    public bool IsPopup { get; }

    public string SaveLabel => LocalizationService.Current.Get(editLoan is null ? LoansKeys.MarkLent : "core.ui.Save");

    [ObservableProperty] private IReadOnlyList<BorrowerDocument> borrowers = [];
    [ObservableProperty] private IReadOnlyList<LoanHistoryRow> history = [];
    [ObservableProperty] private BorrowerDocument? selectedBorrower;
    [ObservableProperty] private LoanHistoryRow? selectedLoan;
    [ObservableProperty] private string loanDate = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    [ObservableProperty] private string dueDate = string.Empty;
    [ObservableProperty] private string note = string.Empty;
    [ObservableProperty] private string error = string.Empty;

    [RelayCommand(CanExecute = nameof(CanManageBorrowers))]
    private async Task ManageBorrowersAsync()
    {
        if (openContactsAsync is not null)
        {
            await openContactsAsync();
            Reload();
        }
    }

    private bool CanManageBorrowers() => openContactsAsync is not null;

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

        if (editLoan is null)
        {
            service.Lend(entryId, SelectedBorrower.Id, lent, due, Note);
        }
        else
        {
            service.Edit(entryId, editLoan.Id, SelectedBorrower.Id, lent, due, Note);
        }
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
            Error = LocalizationService.Current.Get(LoansKeys.LoanInvalidEdit);
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
            ? localization.Get(loan.IsOverdue(DateOnly.FromDateTime(DateTime.Today)) ? LoansKeys.OverdueLoans : LoansKeys.ActiveLoans)
            : localization.Format(LoansKeys.LoanReturnedOn, loan.ReturnDate!);

        return string.Join(Environment.NewLine, new[]
        {
            borrower,
            detail,
            loan.LoanDate is { } loanDate ? localization.Format(LoansKeys.LoanDateValue, loanDate) : null,
            loan.DueDate is { } dueDate ? localization.Format(LoansKeys.LoanDueValue, dueDate) : null,
            loan.Note
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string EditDate(string? value) =>
        LoanDocument.TryReadDate(value, out var date)
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value ?? string.Empty;

    private static bool TryDate(string? input, out DateOnly date) =>
        DateOnly.TryParseExact(input?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
