using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Collections;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.UndoRedo;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

public sealed partial class CollectionsDialogViewModel : ObservableObject
{
    private readonly VirtualCollectionService? service;
    private readonly CatalogUndoManager? undoManager;
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly VirtualCollectionDocumentRepository? collectionRepository;
    private readonly Action? onChanged;
    private readonly Action<CatalogDocument>? navigate;
    private readonly Func<IReadOnlyList<string>>? selectedEntryIdsProvider;
    private global::Couchbase.Lite.ListenerToken? catalogListener;

    public void WatchCatalogChanges(Action<Action> dispatch)
    {
        StopWatchingCatalogChanges();
        if (catalogRepository is not null)
        {
            catalogListener = catalogRepository.Collection.AddChangeListener((_, _) => dispatch(() =>
            {
                if (catalogListener is not null)
                {
                    RefreshCollections();
                }
            }));
        }
    }

    public void StopWatchingCatalogChanges()
    {
        if (catalogListener is { } listener)
        {
            catalogRepository?.Collection.RemoveChangeListener(listener);
            catalogListener = null;
        }
    }

    public CollectionsDialogViewModel(
        VirtualCollectionService? service = null,
        CatalogUndoManager? undoManager = null,
        CatalogDocumentRepository? catalogRepository = null,
        VirtualCollectionDocumentRepository? collectionRepository = null,
        Action? onChanged = null,
        Action<CatalogDocument>? navigate = null,
        Func<IReadOnlyList<string>>? selectedEntryIdsProvider = null)
    {
        this.service = service;
        this.undoManager = undoManager;
        this.catalogRepository = catalogRepository;
        this.collectionRepository = collectionRepository;
        this.onChanged = onChanged;
        this.navigate = navigate;
        this.selectedEntryIdsProvider = selectedEntryIdsProvider;
        RefreshCollections();
    }

    public ObservableCollection<CollectionListItemViewModel> Collections { get; } = [];

    public ObservableCollection<CollectionMemberViewModel> Members { get; } = [];

    public ObservableCollection<CollectionMemberViewModel> FilteredMembers { get; } = [];

    /// <summary>Description of the saved search for a dynamic collection; null when not dynamic.</summary>
    public string? SavedQueryDescription => savedQueryDescription;

    /// <summary>True when a saved search description is available.</summary>
    public bool HasSavedQueryDescription => SavedQueryDescription is not null;

    /// <summary>True when a dynamic collection has no usable saved search.</summary>
    public bool IsMissingSavedQuery => missingSavedQuery;

    /// <summary>True when the selected collection evaluates a saved search instead of stored members.</summary>
    public bool IsDynamicSelected => SelectedCollection?.IsDynamic == true;

    /// <summary>True when no collection is selected and the members pane can only invite one.</summary>
    public bool ShowSelectCollectionHint => CollectionsAvailable && SelectedCollection is null;

    /// <summary>True when the selected manual collection holds no members to show.</summary>
    public bool ShowNoMembersHint =>
        SelectedCollection is { IsDynamic: false } && Members.Count == 0 && MemberFilterText.Trim().Length == 0;

    /// <summary>True when the member filter hides every member of a non-empty list.</summary>
    public bool ShowNoFilterMatchHint =>
        SelectedCollection is not null && Members.Count > 0 && FilteredMembers.Count == 0 && !IsMissingSavedQuery;

    /// <summary>Missing-reference notice bound in the members header; empty when nothing is missing.</summary>
    public string MissingMembersNotice => HasMissingMembers
        ? $"{UnavailableCount} {LocalizationService.Current.Get(UiKeys.MissingEntries)}"
        : string.Empty;

    /// <summary>Notice shown when a dynamic collection has no usable saved search.</summary>
    public string? NoSavedSearchNotice => IsMissingSavedQuery
        ? LocalizationService.Current.Get(TextKeys.CollectionNoSavedSearch)
        : null;

    /// <summary>Prompt naming the member pending removal; shown inside the confirmation bar.</summary>
    public string RemoveMemberConfirmationPrompt => LocalizationService.Current.Format(
        TextKeys.CollectionRemoveMemberConfirm,
        SelectedMember?.Name ?? string.Empty);

    /// <summary>Prompt naming the collection pending deletion; shown inside the confirmation bar.</summary>
    public string DeleteCollectionConfirmationPrompt => LocalizationService.Current.Format(
        TextKeys.CollectionDeleteConfirm,
        SelectedCollection?.Name ?? string.Empty);

    [ObservableProperty]
    private CollectionListItemViewModel? selectedCollection;

    [ObservableProperty]
    private string newCollectionName = string.Empty;

    [ObservableProperty]
    private string renameTo = string.Empty;

    [ObservableProperty]
    private CollectionMemberViewModel? selectedMember;

    [ObservableProperty]
    private string memberFilterText = string.Empty;

    [ObservableProperty]
    private bool isRemoveMemberConfirmationVisible;

    [ObservableProperty]
    private bool isDeleteCollectionConfirmationVisible;

    private string? savedQueryDescription;

    private bool missingSavedQuery;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool dialogAccepted;

    [ObservableProperty]
    private int unavailableCount;

    [ObservableProperty]
    private bool hasMissingMembers;

    public bool CollectionsAvailable => service is not null;

    public bool HasCollections => Collections.Count > 0;

    public bool ShowEmptyCollectionsState => CollectionsAvailable && !HasCollections;

    partial void OnSelectedCollectionChanged(CollectionListItemViewModel? value)
    {
        if (value is not null)
        {
            RenameTo = value.Name;
        }

        IsRemoveMemberConfirmationVisible = false;
        IsDeleteCollectionConfirmationVisible = false;
        RefreshMembers();
        OnPropertyChanged(nameof(IsDynamicSelected));
        OnPropertyChanged(nameof(ShowSelectCollectionHint));
        OnPropertyChanged(nameof(ShowNoMembersHint));
        OnPropertyChanged(nameof(ShowNoFilterMatchHint));
        OnPropertyChanged(nameof(DeleteCollectionConfirmationPrompt));
        NavigateToSelectedCommand.NotifyCanExecuteChanged();
        RenameCollectionCommand.NotifyCanExecuteChanged();
        DeleteCollectionCommand.NotifyCanExecuteChanged();
        RequestRemoveSelectedMemberCommand.NotifyCanExecuteChanged();
        OpenMemberFolderCommand.NotifyCanExecuteChanged();
        AddSelectedEntriesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMemberChanged(CollectionMemberViewModel? value)
    {
        IsRemoveMemberConfirmationVisible = false;
        OnPropertyChanged(nameof(RemoveMemberConfirmationPrompt));
        NavigateToSelectedCommand.NotifyCanExecuteChanged();
        RequestRemoveSelectedMemberCommand.NotifyCanExecuteChanged();
        OpenMemberFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnMemberFilterTextChanged(string value) => RefreshFilteredMembers();

    public void RefreshCollections()
    {
        var selectedId = SelectedCollection?.Id;
        Collections.Clear();
        if (service is not null)
        {
            foreach (var collection in service.GetCollections())
            {
                var (count, missing, _) = CountMembers(collection);
                Collections.Add(new CollectionListItemViewModel(
                    collection.Id, collection.Name, collection.Kind, count)
                {
                    MissingEntryCount = missing,
                    EntrySummary = FormatEntrySummary(collection.Kind, count, missing)
                });
            }
        }

        SelectedCollection = Collections.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal))
            ?? Collections.FirstOrDefault();
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(ShowEmptyCollectionsState));
        OnPropertyChanged(nameof(ShowSelectCollectionHint));
    }

    /// <summary>Selects the collection with the given id when it is listed.</summary>
    public void SelectCollectionById(string collectionId)
    {
        if (string.IsNullOrWhiteSpace(collectionId))
        {
            return;
        }

        var match = Collections.FirstOrDefault(item => string.Equals(item.Id, collectionId, StringComparison.Ordinal));
        if (match is not null)
        {
            SelectedCollection = match;
        }
    }

    private (int Total, int Missing, int Available) CountMembers(VirtualCollectionDocument collection)
    {
        if (service is null)
        {
            return (0, 0, 0);
        }

        if (collection.Kind == VirtualCollectionKind.Dynamic)
        {
            try
            {
                return (service.ResolveEntries(collection.Id, out _).Count, 0, 0);
            }
            catch (InvalidOperationException exception)
            {
                StatusText = exception.Message;
                return (0, 0, 0);
            }
        }

        var total = collection.EntryIds.Count;
        var available = 0;
        foreach (var id in collection.EntryIds)
        {
            if (catalogRepository?.Get(id) is not null)
            {
                available++;
            }
        }

        return (total, total - available, available);
    }

    private void RefreshMembers()
    {
        Members.Clear();
        HasMissingMembers = false;
        UnavailableCount = 0;
        savedQueryDescription = null;
        missingSavedQuery = false;
        var current = SelectedCollection;
        if (service is null || catalogRepository is null || current is null)
        {
            RefreshFilteredMembers();
            return;
        }

        if (current.IsDynamic)
        {
            RefreshSavedQueryState(current.Id);
        }

        IReadOnlyList<CatalogDocument> resolved;
        int missing;
        try
        {
            resolved = service.ResolveEntries(current.Id, out missing);
        }
        catch (InvalidOperationException exception)
        {
            StatusText = exception.Message;
            missingSavedQuery = current.IsDynamic;
            OnPropertyChanged(nameof(IsMissingSavedQuery));
            OnPropertyChanged(nameof(MissingMembersNotice));
            OnPropertyChanged(nameof(NoSavedSearchNotice));
            RefreshFilteredMembers();
            return;
        }
        UnavailableCount = missing;
        HasMissingMembers = missing > 0;
        foreach (var entry in resolved)
        {
            Members.Add(new CollectionMemberViewModel(entry.Id, entry.Name, FullPathOf(entry)));
        }

        var item = Collections.FirstOrDefault(candidate => candidate.Id == current.Id);
        if (item is not null)
        {
            item.EntryCount = resolved.Count + missing;
            item.MissingEntryCount = missing;
            item.EntrySummary = FormatEntrySummary(current.Kind, item.EntryCount, missing);
        }

        OnPropertyChanged(nameof(SavedQueryDescription));
        OnPropertyChanged(nameof(HasSavedQueryDescription));
        OnPropertyChanged(nameof(IsMissingSavedQuery));
        OnPropertyChanged(nameof(MissingMembersNotice));
        OnPropertyChanged(nameof(NoSavedSearchNotice));
        RefreshFilteredMembers();
    }

    private void RefreshSavedQueryState(string collectionId)
    {
        try
        {
            var query = service!.GetDynamicQuery(collectionId);
            savedQueryDescription = LocalizationService.Current.Format(
                TextKeys.CollectionSavedSearchDescription,
                CountPredicates(query.Root));
        }
        catch (InvalidOperationException)
        {
            savedQueryDescription = null;
            missingSavedQuery = true;
        }
    }

    private static int CountPredicates(CatalogQueryGroup group) =>
        group.Predicates.Count + group.Groups.Sum(CountPredicates);

    private static string FormatEntrySummary(VirtualCollectionKind kind, int total, int missing)
    {
        var entries = LocalizationService.Current.Get(UiKeys.CollectionEntries);
        if (kind == VirtualCollectionKind.Dynamic || missing <= 0)
        {
            return $"{total} {entries}";
        }

        var unavailable = LocalizationService.Current.Get(UiKeys.MissingEntries);
        return $"{total} {entries} · {missing} {unavailable}";
    }

    private void RefreshFilteredMembers()
    {
        FilteredMembers.Clear();
        var filter = MemberFilterText.Trim();
        foreach (var member in Members)
        {
            if (filter.Length == 0
                || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || member.FullPath.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredMembers.Add(member);
            }
        }

        OnPropertyChanged(nameof(ShowNoMembersHint));
        OnPropertyChanged(nameof(ShowNoFilterMatchHint));
    }

    private string FullPathOf(CatalogDocument entry)
    {
        if (catalogRepository is null)
        {
            return entry.Name;
        }

        var segments = new Stack<string>();
        segments.Push(entry.Name);
        var parentId = entry.ParentId;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(parentId) && guard++ < 1000)
        {
            var parent = catalogRepository.Get(parentId);
            if (parent is null)
            {
                break;
            }

            segments.Push(parent.Name);
            parentId = parent.ParentId;
        }

        return string.Join("/", segments);
    }

    private bool CanManageCollections() => service is not null && collectionRepository is not null;

    private bool CanManageSelectedCollection() => CanManageCollections() && SelectedCollection is not null;

    [RelayCommand(CanExecute = nameof(CanManageCollections))]
    private void CreateCollection()
    {
        if (service is null || collectionRepository is null)
        {
            return;
        }

        var name = NewCollectionName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.CollectionNameRequired);
            return;
        }

        try
        {
            var created = service.CreateCollection(name);
            undoManager?.RecordCollectionCreated(collectionRepository, created.Id, created.Name);
            NewCollectionName = string.Empty;
            StatusText = LocalizationService.Current.Format(TextKeys.CollectionCreated, created.Name);
            RefreshCollections();
            SelectCollection(created.Id);
            Changed();
        }
        catch (Exception exception) when (exception is CollectionNameRequiredException
            || exception is CollectionDuplicateNameException)
        {
            StatusText = exception.Message;
        }
    }

    /// <summary>Creates a collection with a unique default name and opens the new row for inline editing.</summary>
    [RelayCommand(CanExecute = nameof(CanManageCollections))]
    private void AddCollection()
    {
        if (service is null || collectionRepository is null)
        {
            return;
        }

        var baseName = LocalizationService.Current.Get(UiKeys.NewCollection);
        var existing = new HashSet<string>(Collections.Select(collection => collection.Name), StringComparer.OrdinalIgnoreCase);
        var name = baseName;
        var suffix = 2;
        while (existing.Contains(name))
        {
            name = $"{baseName} {suffix++}";
        }

        try
        {
            var created = service.CreateCollection(name);
            undoManager?.RecordCollectionCreated(collectionRepository, created.Id, created.Name);
            StatusText = LocalizationService.Current.Format(TextKeys.CollectionCreated, created.Name);
            RefreshCollections();
            SelectCollection(created.Id);
            if (SelectedCollection is not null)
            {
                SelectedCollection.RenameDraft = SelectedCollection.Name;
                SelectedCollection.IsRenaming = true;
            }

            Changed();
        }
        catch (Exception exception) when (exception is CollectionNameRequiredException
            || exception is CollectionDuplicateNameException)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedCollection))]
    private void RenameCollection()
    {
        if (SelectedCollection is null)
        {
            return;
        }

        if (RenameCollectionCore(SelectedCollection, RenameTo))
        {
            SelectCollection(SelectedCollection.Id);
        }
    }

    /// <summary>Shows the inline delete confirmation bar for the selected collection.</summary>
    [RelayCommand(CanExecute = nameof(CanManageSelectedCollection))]
    private void DeleteCollection()
    {
        OnPropertyChanged(nameof(DeleteCollectionConfirmationPrompt));
        IsDeleteCollectionConfirmationVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedCollection))]
    private void ConfirmDeleteCollection()
    {
        if (service is null || collectionRepository is null || SelectedCollection is null)
        {
            return;
        }

        var current = SelectedCollection;
        var snapshot = new VirtualCollectionDocument
        {
            Id = current.Id,
            Name = current.Name,
            Kind = current.Kind,
            EntryIds = service.GetEntryIds(current.Id).ToList(),
            SavedQueryJson = service.GetRequired(current.Id).SavedQueryJson
        };

        try
        {
            service.DeleteCollection(current.Id);
            undoManager?.RecordCollectionDeleted(collectionRepository, snapshot);
            StatusText = LocalizationService.Current.Format(TextKeys.CollectionDeleted, current.Name);
            IsDeleteCollectionConfirmationVisible = false;
            RefreshCollections();
            Changed();
        }
        catch (CollectionNotFoundException exception)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand]
    private void CancelDeleteCollection() => IsDeleteCollectionConfirmationVisible = false;

    /// <summary>Starts inline renaming for the given row.</summary>
    [RelayCommand(CanExecute = nameof(CanManageCollections))]
    private void BeginRenameCollection(CollectionListItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        foreach (var other in Collections)
        {
            other.IsRenaming = false;
        }

        item.RenameDraft = item.Name;
        item.IsRenaming = true;
    }

    /// <summary>Commits the inline rename draft for the given row.</summary>
    [RelayCommand(CanExecute = nameof(CanManageCollections))]
    private void CommitRenameCollection(CollectionListItemViewModel? item)
    {
        if (item is null || !item.IsRenaming)
        {
            return;
        }

        if (RenameCollectionCore(item, item.RenameDraft))
        {
            item.IsRenaming = false;
            SelectCollection(item.Id);
        }
    }

    /// <summary>Discards the inline rename draft for the given row.</summary>
    [RelayCommand]
    private void CancelRenameCollection(CollectionListItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsRenaming = false;
        }
    }

    private bool RenameCollectionCore(CollectionListItemViewModel current, string? name)
    {
        if (service is null || collectionRepository is null)
        {
            return false;
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.CollectionNameRequired);
            return false;
        }

        if (string.Equals(current.Name, trimmed, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            service.RenameCollection(current.Id, trimmed);
            undoManager?.RecordCollectionRenamed(collectionRepository, current.Id, current.Name, trimmed);
            current.Name = trimmed;
            StatusText = LocalizationService.Current.Format(TextKeys.CollectionRenamed, trimmed);
            RefreshCollections();
            Changed();
            return true;
        }
        catch (Exception exception) when (exception is CollectionNameRequiredException
            || exception is CollectionDuplicateNameException
            || exception is CollectionNotFoundException)
        {
            StatusText = exception.Message;
            return false;
        }
    }

    private bool CanCleanMissing() => service is not null && SelectedCollection is not null && HasMissingMembers;

    [RelayCommand(CanExecute = nameof(CanCleanMissing))]
    private void RemoveMissingEntries()
    {
        if (service is null || SelectedCollection is null)
        {
            return;
        }

        var removed = service.PruneDeletedReferences(SelectedCollection.Id);
        StatusText = LocalizationService.Current.Format(TextKeys.CollectionCleaned, removed);
        RefreshCollections();
        SelectCollection(SelectedCollection.Id);
        Changed();
    }

    private bool CanNavigate() => navigate is not null && SelectedMember is not null;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void NavigateToSelected()
    {
        if (navigate is not null && catalogRepository is not null && SelectedMember is not null)
        {
            var document = catalogRepository.Get(SelectedMember.Id);
            if (document is not null)
            {
                navigate(document);
            }
        }
    }

    private bool CanOpenMemberFolder() => CanNavigate();

    [RelayCommand(CanExecute = nameof(CanOpenMemberFolder))]
    private void OpenMemberFolder()
    {
        if (navigate is null || catalogRepository is null || SelectedMember is null)
        {
            return;
        }

        var document = catalogRepository.Get(SelectedMember.Id);
        if (document is null)
        {
            return;
        }

        var folder = document.ParentId is { } parentId ? catalogRepository.Get(parentId) : null;
        navigate(folder ?? document);
    }

    private bool CanRemoveSelectedMember() =>
        service is not null
        && collectionRepository is not null
        && SelectedCollection is { IsDynamic: false }
        && SelectedMember is not null;

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedMember))]
    private void RequestRemoveSelectedMember() => IsRemoveMemberConfirmationVisible = true;

    [RelayCommand]
    private void ConfirmRemoveSelectedMember()
    {
        if (service is null || collectionRepository is null || SelectedCollection is null || SelectedMember is null)
        {
            return;
        }

        var current = SelectedCollection;
        var member = SelectedMember;
        if (current.IsDynamic)
        {
            return;
        }

        var oldIds = service.GetEntryIds(current.Id);
        service.RemoveEntries(current.Id, [member.Id]);
        undoManager?.RecordCollectionEntriesChanged(
            collectionRepository,
            current.Id,
            oldIds,
            service.GetEntryIds(current.Id));
        StatusText = LocalizationService.Current.Format(TextKeys.CollectionMemberRemoved, member.Name, current.Name);
        IsRemoveMemberConfirmationVisible = false;
        RefreshCollections();
        SelectCollection(current.Id);
        Changed();
    }

    [RelayCommand]
    private void CancelRemoveMember() => IsRemoveMemberConfirmationVisible = false;

    private bool CanAddSelectedEntries() =>
        service is not null
        && collectionRepository is not null
        && SelectedCollection is { IsDynamic: false };

    /// <summary>Adds the catalog entries currently selected in the main browser to the manual collection.</summary>
    [RelayCommand(CanExecute = nameof(CanAddSelectedEntries))]
    private void AddSelectedEntries()
    {
        if (service is null || collectionRepository is null || SelectedCollection is null)
        {
            return;
        }

        var current = SelectedCollection;
        if (current.IsDynamic)
        {
            return;
        }

        var ids = selectedEntryIdsProvider?.Invoke() ?? [];
        if (ids.Count == 0)
        {
            StatusText = LocalizationService.Current.Get(TextKeys.CollectionSelectEntriesFirst);
            return;
        }

        var oldIds = service.GetEntryIds(current.Id);
        service.AddEntries(current.Id, ids);
        undoManager?.RecordCollectionEntriesChanged(
            collectionRepository,
            current.Id,
            oldIds,
            service.GetEntryIds(current.Id));
        StatusText = LocalizationService.Current.Format(TextKeys.CollectionAddedTo, current.Name, ids.Count);
        RefreshCollections();
        SelectCollection(current.Id);
        Changed();
    }

    partial void OnHasMissingMembersChanged(bool value)
    {
        RemoveMissingEntriesCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(MissingMembersNotice));
    }

    private void SelectCollection(string id)
    {
        SelectedCollection = Collections.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
    }

    private void Changed()
    {
        RefreshMembers();
        onChanged?.Invoke();
    }

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }
}
