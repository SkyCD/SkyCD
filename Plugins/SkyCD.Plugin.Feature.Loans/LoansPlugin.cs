using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Couchbase.Lite;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities;
using SkyCD.Plugin.Abstractions.Capabilities.Catalog;
using SkyCD.Plugin.Abstractions.Capabilities.Import;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using SkyCD.Plugin.Abstractions.Capabilities.Ui;
using SkyCD.Plugin.Abstractions.Localization;
using SkyCD.Plugin.Feature.Loans.Import;
using SkyCD.Plugin.Feature.Loans.Models;
using SkyCD.Plugin.Feature.Loans.Views;
using SkyCD.Presentation.Catalog;
using SkyCD.UI.Localization;
using YamlDotNet.Serialization;

namespace SkyCD.Plugin.Feature.Loans;

/// <summary>
/// First-party catalog feature plugin that tracks item loans and borrowers. The storage, service, view
/// models, views and localization live in this assembly; the host only renders the contributions.
/// </summary>
public sealed class LoansPlugin : IPluginCapability, ICatalogRepositoryConsumer, ICatalogFeaturePluginCapability,
    ICatalogImportPluginCapability, ICatalogLifecyclePluginCapability, IMenuPluginCapability, IHostDialogConsumer,
    ILocalizationPluginCapability
{
    private const string LendCommandId = "skycd.loans.lend";
    private const string ReturnCommandId = "skycd.loans.return";
    private const string HistoryCommandId = "skycd.loans.history";
    private const string ContactsCommandId = "skycd.loans.contacts";
    private const string EntryIdProperty = "EntryId";

    private readonly Dictionary<string, (string Borrower, bool Overdue)> activeLoans = new(StringComparer.Ordinal);
    private ICatalogRepositoryProvider? provider;
    private IHostDialogApi? dialogApi;
    private CatalogDocumentRepository? cachedRepository;
    private CatalogLoanService? cachedService;

    /// <inheritdoc />
    public event EventHandler? DataChanged;

    /// <inheritdoc />
    public void InitializeCatalogRepository(ICatalogRepositoryProvider catalogProvider)
    {
        ArgumentNullException.ThrowIfNull(catalogProvider);
        provider = catalogProvider;
        catalogProvider.Changed += OnCatalogChanged;
    }

    /// <inheritdoc />
    public void InitializeDialogApi(IHostDialogApi hostDialogApi)
    {
        dialogApi = hostDialogApi;
    }

    /// <inheritdoc />
    public void OnCatalogLifecycle(CatalogLifecycle lifecycle, string? catalogPath)
    {
        if (lifecycle is CatalogLifecycle.Opened or CatalogLifecycle.Changed)
        {
            SeedStatusVariant();
        }

        Invalidate();
    }

    /// <inheritdoc />
    public IReadOnlyList<EntryDecoration> GetEntryDecorations(IReadOnlyList<CatalogEntry> entries)
    {
        RefreshActiveLoans();
        return activeLoans.Count == 0
            ? []
            : [.. entries.Where(entry => activeLoans.ContainsKey(entry.Id)).Select(entry =>
            {
                var state = activeLoans[entry.Id];
                return new EntryDecoration(
                    entry.Id,
                    LocalizationService.Current.Get(state.Overdue ? LoansKeys.LoanOverdueBadge : LoansKeys.LoanBadge),
                    "↗",
                    state.Borrower,
                    state.Overdue);
            })];
    }

    /// <inheritdoc />
    public IReadOnlyList<DetailsColumnContribution> GetDetailsColumns() =>
    [
        new(
            "skycd.loans.lentTo",
            LocalizationService.Current.Get(LoansKeys.LentTo),
            55,
            entry => activeLoans.TryGetValue(entry.Id, out var state) ? state.Borrower : null)
    ];

    /// <inheritdoc />
    public IReadOnlyList<AdvancedSearchFieldContribution> GetAdvancedSearchFields()
    {
        RefreshActiveLoans();
        string? Borrower(string entryId) =>
            activeLoans.TryGetValue(entryId, out var state) ? state.Borrower : null;
        return
        [
            new(
                "skycd.loans.active",
                LocalizationService.Current.Get(LoansKeys.ActiveLoans),
                entry => activeLoans.ContainsKey(entry.Id)),
            new(
                "skycd.loans.overdue",
                LocalizationService.Current.Get(LoansKeys.OverdueLoans),
                entry => activeLoans.TryGetValue(entry.Id, out var state) && state.Overdue),
            new(
                "skycd.loans.borrower",
                LocalizationService.Current.Get(LoansKeys.Borrower),
                entry => Borrower(entry.Id) is not null,
                entry => Borrower(entry.Id))
        ];
    }

    /// <inheritdoc />
    public IReadOnlyList<EntryCommandContribution> GetEntryCommands() =>
    [
        new(LendCommandId, LocalizationService.Current.Get(LoansKeys.MarkLent), 50),
        new(ReturnCommandId, LocalizationService.Current.Get(LoansKeys.MarkReturned), 51),
        new(HistoryCommandId, LocalizationService.Current.Get(LoansKeys.LoanHistory), 52)
    ];

    /// <inheritdoc />
    public async Task<bool> ExecuteEntryCommandAsync(string commandId, CatalogEntry entry,
        CancellationToken cancellationToken = default)
    {
        if (Service() is not { } service)
        {
            return false;
        }

        var editor = new LoanDialogViewModel(service, entry.Id, Invalidate);
        if (commandId == ReturnCommandId && editor.SelectedLoan is { IsActive: true })
        {
            editor.MarkReturnedCommand.Execute(null);
            return editor.Error.Length == 0;
        }

        await ShowAsync(LocalizationService.Current.Get(LoansKeys.Loans), 411, 460, editor).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
    [
        new(LendCommandId, LocalizationService.Current.Get(LoansKeys.MarkLent), "Loans", 10),
        new(ReturnCommandId, LocalizationService.Current.Get(LoansKeys.MarkReturned), "Loans", 20),
        new(HistoryCommandId, LocalizationService.Current.Get(LoansKeys.LoanHistory), "Loans", 30),
        new(ContactsCommandId, LocalizationService.Current.Get(LoansKeys.ManageContacts), "Loans", 40)
    ];

    /// <inheritdoc />
    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (Service() is not { } service)
        {
            return;
        }

        if (commandId == ContactsCommandId)
        {
            await ShowAsync(LocalizationService.Current.Get(LoansKeys.Contacts),
                640, 520, new ContactsDialogViewModel(service, Invalidate)).ConfigureAwait(false);
            return;
        }

        if (context.Properties?.GetValueOrDefault(EntryIdProperty) is not { Length: > 0 } entryId)
        {
            return;
        }

        await ExecuteEntryCommandAsync(commandId,
            new CatalogEntry(entryId, string.Empty, string.Empty, null, CatalogEntryKind.Other, 0,
                new Dictionary<string, string>()), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ImportOutcome?> OnEntriesImportedAsync(IReadOnlyList<CatalogEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (Service() is not { } service || entries.Count == 0)
        {
            return null;
        }

        var documents = entries.Select(entry =>
        {
            var properties = new PropertiesCollection();
            foreach (var (key, value) in entry.Properties)
            {
                properties[key] = value;
            }

            return new CatalogDocument
            {
                Id = entry.Id,
                Name = entry.Name,
                ParentId = entry.ParentId,
                Size = entry.Size,
                Properties = properties
            };
        }).ToArray();

        return await Task.Run(() =>
        {
            var warnings = new ImportedLoanMapper(service).Import(documents);
            if (warnings.Count > 0)
            {
                Invalidate();
            }

            return new ImportOutcome(warnings, documents.Length);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetTranslations() => Translations;

    private static readonly Dictionary<string, string> English = new()
    {
        ["ui.Loans"] = "Loans",
        ["ui.Borrower"] = "Borrower",
        ["ui.BorrowerName"] = "Borrower name",
        ["ui.AddBorrower"] = "Add borrower",
        ["ui.RenameBorrower"] = "Rename borrower",
        ["ui.LoanDateLabel"] = "Loan date",
        ["ui.LoanDueLabel"] = "Due date (optional)",
        ["ui.LoanNote"] = "Loan note (optional)",
        ["ui.MarkLent"] = "Mark as lent",
        ["ui.MarkReturned"] = "Mark as returned",
        ["ui.LoanHistory"] = "Loan history",
        ["ui.LoanChangesSave"] = "Loan and borrower actions save immediately.",
        ["ui.LoanInvalidEdit"] = "Check the borrower, loan status, and dates before saving.",
        ["ui.ActiveLoans"] = "Active loans",
        ["ui.OverdueLoans"] = "Overdue loans",
        ["ui.LentTo"] = "Lent to",
        ["ui.LoanReturnedOn"] = "Returned {0}",
        ["ui.LoanDateValue"] = "Lent {0}",
        ["ui.LoanDueValue"] = "Due {0}",
        ["ui.LoanBadge"] = "Lent",
        ["ui.LoanOverdueBadge"] = "Overdue",
        ["ui.ContactLentItems"] = "Lent items",
        ["ui.NoLentItems"] = "No items are currently lent to this contact.",
        ["ui.ManageContacts"] = "Manage contacts",
        ["ui.Contacts"] = "Contacts",
        ["ui.ContactDetails"] = "Details",
        ["ui.ContactDisplayName"] = "Display name",
        ["ui.ContactPhone"] = "Phone",
        ["ui.ContactFax"] = "Fax",
        ["ui.ContactEmail"] = "E-mail",
        ["ui.ContactWebPage"] = "Web page",
        ["ui.ContactMessenger"] = "Messenger",
        ["ui.ContactAddress"] = "Address",
        ["ui.ContactCompany"] = "Company",
        ["ui.ContactPosition"] = "Position",
        ["ui.NewContact"] = "New contact",
        ["ui.DeleteContact"] = "Delete contact",
        ["ui.SaveContact"] = "Save contact",
        ["ui.ImportContacts"] = "Import vCard…",
        ["ui.ExportContacts"] = "Export vCard…",
        ["ui.ContactNameRequired"] = "Contact name is required.",
        ["ui.ContactNameDuplicate"] = "Another contact already uses this name.",
        ["ui.ContactInUse"] = "This contact has history and cannot be deleted.",
        ["ui.ContactSaved"] = "Contact saved.",
        ["ui.ContactDeleted"] = "Contact deleted.",
        ["ui.ContactsImported"] = "Imported {0} contacts ({1} updated).",
        ["ui.ContactsImportEmpty"] = "The selected vCard file did not contain any contacts.",
        ["ui.ContactsImportFailed"] = "Could not read the vCard file: {0}",
        ["ui.ContactsExported"] = "Exported {0} contacts.",
        ["ui.ContactsExportFailed"] = "Could not write the vCard file: {0}",
        ["ui.VCardFiles"] = "vCard files"
    };

    private static readonly Dictionary<string, string> Lithuanian = new()
    {
        ["ui.Loans"] = "Paskolos",
        ["ui.Borrower"] = "Skolininkas",
        ["ui.BorrowerName"] = "Skolininko vardas",
        ["ui.AddBorrower"] = "Pridėti skolininką",
        ["ui.RenameBorrower"] = "Pervadinti skolininką",
        ["ui.LoanDateLabel"] = "Paskolinimo data",
        ["ui.LoanDueLabel"] = "Grąžinimo data (neprivaloma)",
        ["ui.LoanNote"] = "Paskolos pastaba (neprivaloma)",
        ["ui.MarkLent"] = "Pažymėti kaip paskolintą",
        ["ui.MarkReturned"] = "Pažymėti kaip grąžintą",
        ["ui.LoanHistory"] = "Paskolų istorija",
        ["ui.LoanChangesSave"] = "Paskolų ir skolininkų veiksmai išsaugomi iš karto.",
        ["ui.LoanInvalidEdit"] = "Prieš išsaugant patikrinkite skolininką, paskolos būseną ir datas.",
        ["ui.ActiveLoans"] = "Aktyvios paskolos",
        ["ui.OverdueLoans"] = "Vėluojančios paskolos",
        ["ui.LentTo"] = "Paskolinta",
        ["ui.LoanReturnedOn"] = "Grąžinta {0}",
        ["ui.LoanDateValue"] = "Paskolinta {0}",
        ["ui.LoanDueValue"] = "Grąžinti iki {0}",
        ["ui.LoanBadge"] = "Paskolinta",
        ["ui.LoanOverdueBadge"] = "Vėluoja",
        ["ui.ContactLentItems"] = "Paskolinti elementai",
        ["ui.NoLentItems"] = "Šiam kontaktui šiuo metu nieko nėra paskolinta.",
        ["ui.ManageContacts"] = "Tvarkyti kontaktus",
        ["ui.Contacts"] = "Kontaktai",
        ["ui.ContactDetails"] = "Informacija",
        ["ui.ContactDisplayName"] = "Rodomas vardas",
        ["ui.ContactPhone"] = "Telefonas",
        ["ui.ContactFax"] = "Faksas",
        ["ui.ContactEmail"] = "El. paštas",
        ["ui.ContactWebPage"] = "Tinklalapis",
        ["ui.ContactMessenger"] = "Pranešimų programa",
        ["ui.ContactAddress"] = "Adresas",
        ["ui.ContactCompany"] = "Įmonė",
        ["ui.ContactPosition"] = "Pareigos",
        ["ui.NewContact"] = "Naujas kontaktas",
        ["ui.DeleteContact"] = "Ištrinti kontaktą",
        ["ui.SaveContact"] = "Išsaugoti kontaktą",
        ["ui.ImportContacts"] = "Importuoti vCard…",
        ["ui.ExportContacts"] = "Eksportuoti vCard…",
        ["ui.ContactNameRequired"] = "Būtinas kontakto vardas.",
        ["ui.ContactNameDuplicate"] = "Kitas kontaktas jau naudoja šį vardą.",
        ["ui.ContactInUse"] = "Šis kontaktas turi istoriją, todėl jo ištrinti negalima.",
        ["ui.ContactSaved"] = "Kontaktas išsaugotas.",
        ["ui.ContactDeleted"] = "Kontaktas ištrintas.",
        ["ui.ContactsImported"] = "Importuota kontaktų: {0} (atnaujinta: {1}).",
        ["ui.ContactsImportEmpty"] = "Pasirinktame vCard faile nebuvo kontaktų.",
        ["ui.ContactsImportFailed"] = "Nepavyko perskaityti vCard failo: {0}",
        ["ui.ContactsExported"] = "Eksportuota kontaktų: {0}.",
        ["ui.ContactsExportFailed"] = "Nepavyko įrašyti vCard failo: {0}",
        ["ui.VCardFiles"] = "vCard failai"
    };

    private static readonly IReadOnlyDictionary<string, string> Translations = new Dictionary<string, string>
    {
        ["en"] = Serialize(English),
        ["lt"] = Serialize(Lithuanian)
    };

    private static string Serialize(Dictionary<string, string> values) =>
        new SerializerBuilder().Build().Serialize(values);

    private void OnCatalogChanged(object? sender, CatalogLifecycle lifecycle) =>
        OnCatalogLifecycle(lifecycle, provider?.CatalogPath);

    private void Invalidate()
    {
        cachedRepository = null;
        cachedService = null;
        activeLoans.Clear();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private CatalogLoanService? Service()
    {
        if (provider?.Repository is not { } repository)
        {
            return null;
        }

        if (!ReferenceEquals(repository, cachedRepository) || cachedService is null)
        {
            cachedRepository = repository;
            cachedService = CatalogLoanService.Open(repository);
        }

        cachedService.CatalogPath = provider.CatalogPath ?? string.Empty;
        return cachedService;
    }

    private void RefreshActiveLoans()
    {
        activeLoans.Clear();
        if (Service() is not { } service)
        {
            return;
        }

        foreach (var (entryId, state) in service.GetActiveLoans(DateOnly.FromDateTime(DateTime.Today)))
        {
            activeLoans[entryId] = state;
        }
    }

    private async Task ShowAsync(string title, int width, int height, object viewModel)
    {
        if (dialogApi is null)
        {
            return;
        }

        await dialogApi.ShowAsync(new HostWindowRequest(title, width, height, () => viewModel switch
        {
            LoanDialogViewModel loan => new LoanEditor { DataContext = loan },
            ContactsDialogViewModel contacts => new ContactsWindow { DataContext = contacts },
            _ => throw new ArgumentOutOfRangeException(nameof(viewModel))
        })).ConfigureAwait(false);
        Invalidate();
    }

    private void SeedStatusVariant()
    {
        if (provider?.Repository?.Collection?.Database is not { } database ||
            Attribute.GetCustomAttribute(typeof(StatusVariantDocument), typeof(CouchbaseDocument))
                is not CouchbaseDocument mapping)
        {
            return;
        }

        var collection = database.GetCollection(mapping.CollectionName, Collection.DefaultScopeName)
            ?? database.CreateCollection(mapping.CollectionName, Collection.DefaultScopeName);
        var repository = new StatusVariantDocumentRepository();
        repository.Initialize(typeof(StatusVariantDocument), mapping.CollectionName, collection);
        if (repository.Get("status-loaned-out") is not null)
        {
            return;
        }

        repository.Save("status-loaned-out", new StatusVariantDocument
        {
            Id = "status-loaned-out",
            Name = "Loaned Out",
            IconGlyph = "Lucide:Handshake",
            IconColor = "#F97316",
            ItemTypes = [CatalogDocumentType.Media, CatalogDocumentType.File]
        });
    }
}
