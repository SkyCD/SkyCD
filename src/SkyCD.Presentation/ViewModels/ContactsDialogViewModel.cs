using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Presentation.Lending;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Contact manager dialog: keeps the contact list, the editable contact
/// details, and the items currently lent to the selected contact. Edits save
/// immediately so the loan dialog and lending views reflect them at once.
/// </summary>
public sealed partial class ContactsDialogViewModel : ObservableObject
{
    private readonly CatalogLoanService service;
    private readonly Action? onChanged;

    public ContactsDialogViewModel(CatalogLoanService service, Action? onChanged = null)
    {
        this.service = service;
        this.onChanged = onChanged;
        Reload();
    }

    public ObservableCollection<ContactListItem> Contacts { get; } = [];

    public ObservableCollection<ContactLentItem> LentItems { get; } = [];

    [ObservableProperty] private ContactListItem? selectedContact;
    [ObservableProperty] private string contactName = string.Empty;
    [ObservableProperty] private string contactDisplayName = string.Empty;
    [ObservableProperty] private string contactComments = string.Empty;
    [ObservableProperty] private string contactPhone = string.Empty;
    [ObservableProperty] private string contactFax = string.Empty;
    [ObservableProperty] private string contactEmail = string.Empty;
    [ObservableProperty] private string contactWebPage = string.Empty;
    [ObservableProperty] private string contactMessenger = string.Empty;
    [ObservableProperty] private string contactAddress = string.Empty;
    [ObservableProperty] private string contactCompany = string.Empty;
    [ObservableProperty] private string contactPosition = string.Empty;
    [ObservableProperty] private string statusText = string.Empty;
    [ObservableProperty] private string error = string.Empty;
    [ObservableProperty] private bool hasLentItems;

    partial void OnSelectedContactChanged(ContactListItem? value) => LoadContact();

    [RelayCommand]
    private void AddContact()
    {
        Error = string.Empty;
        try
        {
            var baseName = LocalizationService.Current.Get(UiKeys.NewContact);
            var existing = service.GetContacts().Select(contact => contact.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = baseName;
            for (var index = 2; existing.Contains(name); index++)
            {
                name = $"{baseName} {index}";
            }

            var contact = service.CreateContact(name);
            Reload(contact.Id);
            StatusText = LocalizationService.Current.Get(UiKeys.ContactSaved);
            onChanged?.Invoke();
        }
        catch (ContactEditException)
        {
            Error = LocalizationService.Current.Get(UiKeys.ContactNameDuplicate);
        }
    }

    [RelayCommand]
    private void SaveContact()
    {
        Error = string.Empty;
        if (SelectedContact is not { } selected)
        {
            return;
        }

        var localization = LocalizationService.Current;
        var name = ContactName.Trim();
        if (name.Length == 0)
        {
            Error = localization.Get(UiKeys.ContactNameRequired);
            return;
        }

        if (service.GetContacts().Any(contact => contact.Id != selected.Id &&
            string.Equals(contact.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            Error = localization.Get(UiKeys.ContactNameDuplicate);
            return;
        }

        try
        {
            var contact = service.GetContact(selected.Id) ?? throw new ContactEditException();
            contact.Name = name;
            contact.DisplayName = ContactDisplayName;
            contact.Comments = ContactComments;
            contact.Phone = ContactPhone;
            contact.Fax = ContactFax;
            contact.Email = ContactEmail;
            contact.WebPage = ContactWebPage;
            contact.Messenger = ContactMessenger;
            contact.Address = ContactAddress;
            contact.Company = ContactCompany;
            contact.Position = ContactPosition;
            service.SaveContact(contact);
            Reload(contact.Id);
            StatusText = localization.Get(UiKeys.ContactSaved);
            onChanged?.Invoke();
        }
        catch (ContactEditException)
        {
            Error = localization.Get(UiKeys.ContactNameDuplicate);
        }
    }

    [RelayCommand]
    private void DeleteContact()
    {
        Error = string.Empty;
        if (SelectedContact is not { } selected)
        {
            return;
        }

        var localization = LocalizationService.Current;
        var index = Contacts.IndexOf(selected);
        try
        {
            service.DeleteContact(selected.Id);
            Reload();
            SelectedContact = Contacts.Count == 0 ? null : Contacts[Math.Min(index, Contacts.Count - 1)];
            StatusText = localization.Get(UiKeys.ContactDeleted);
            onChanged?.Invoke();
        }
        catch (ContactEditException)
        {
            Error = localization.Get(UiKeys.ContactInUse);
        }
    }

    public void Import(string text)
    {
        Error = string.Empty;
        var localization = LocalizationService.Current;
        try
        {
            var parsed = VCardCodec.Parse(text);
            if (parsed.Count == 0)
            {
                StatusText = localization.Get(UiKeys.ContactsImportEmpty);
                return;
            }

            var result = service.ImportContacts(parsed);
            Reload(SelectedContact?.Id);
            StatusText = localization.Format(UiKeys.ContactsImported, result.Added, result.Updated);
            onChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Error = localization.Format(UiKeys.ContactsImportFailed, exception.Message);
        }
    }

    public string Export()
    {
        Error = string.Empty;
        var localization = LocalizationService.Current;
        try
        {
            var text = service.ExportContacts();
            StatusText = localization.Format(UiKeys.ContactsExported, service.GetContacts().Count);
            return text;
        }
        catch (Exception exception)
        {
            Error = localization.Format(UiKeys.ContactsExportFailed, exception.Message);
            return string.Empty;
        }
    }

    private void Reload(string? selectId = null)
    {
        selectId ??= SelectedContact?.Id;
        Contacts.Clear();
        foreach (var contact in service.GetContacts())
        {
            var display = string.IsNullOrWhiteSpace(contact.DisplayName) ? contact.Name : contact.DisplayName;
            Contacts.Add(new ContactListItem(contact.Id, contact.Name, display));
        }

        SelectedContact = Contacts.FirstOrDefault(contact => contact.Id == selectId) ?? Contacts.FirstOrDefault();
        if (SelectedContact is null)
        {
            LoadContact();
        }
    }

    private void LoadContact()
    {
        Error = string.Empty;
        LentItems.Clear();
        var contact = SelectedContact is { } selected ? service.GetContact(selected.Id) : null;
        if (contact is null)
        {
            ContactName = string.Empty;
            ContactDisplayName = string.Empty;
            ContactComments = string.Empty;
            ContactPhone = string.Empty;
            ContactFax = string.Empty;
            ContactEmail = string.Empty;
            ContactWebPage = string.Empty;
            ContactMessenger = string.Empty;
            ContactAddress = string.Empty;
            ContactCompany = string.Empty;
            ContactPosition = string.Empty;
            HasLentItems = false;
            return;
        }

        ContactName = contact.Name;
        ContactDisplayName = contact.DisplayName;
        ContactComments = contact.Comments;
        ContactPhone = contact.Phone;
        ContactFax = contact.Fax;
        ContactEmail = contact.Email;
        ContactWebPage = contact.WebPage;
        ContactMessenger = contact.Messenger;
        ContactAddress = contact.Address;
        ContactCompany = contact.Company;
        ContactPosition = contact.Position;

        var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var loan in service.GetActiveLoansForContact(contact.Id))
        {
            var parts = new List<string> { service.GetEntryName(loan.EntryId) };
            if (loan.LoanDate is { } loanDate)
            {
                parts.Add(LocalizationService.Current.Format(UiKeys.LoanDateValue, loanDate));
            }

            if (loan.DueDate is { } dueDate)
            {
                parts.Add(LocalizationService.Current.Format(UiKeys.LoanDueValue, dueDate));
            }

            if (loan.IsOverdue(today))
            {
                parts.Add(LocalizationService.Current.Get(UiKeys.OverdueLoans));
            }

            LentItems.Add(new ContactLentItem(loan.EntryId, string.Join(" — ", parts)));
        }

        HasLentItems = LentItems.Count > 0;
    }
}
