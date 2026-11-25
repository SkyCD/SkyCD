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
        Action<CatalogDocument>? navigate = null)
    {
        this.service = service;
        this.undoManager = undoManager;
        this.catalogRepository = catalogRepository;
        this.collectionRepository = collectionRepository;
        this.onChanged = onChanged;
        this.navigate = navigate;
        RefreshCollections();
    }

    public ObservableCollection<CollectionListItemViewModel> Collections { get; } = [];

    public ObservableCollection<CollectionMemberViewModel> Members { get; } = [];

    [ObservableProperty]
    private CollectionListItemViewModel? selectedCollection;

    [ObservableProperty]
    private string newCollectionName = string.Empty;

    [ObservableProperty]
    private string renameTo = string.Empty;

    [ObservableProperty]
    private CollectionMemberViewModel? selectedMember;

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

        RefreshMembers();
        NavigateToSelectedCommand.NotifyCanExecuteChanged();
        RenameCollectionCommand.NotifyCanExecuteChanged();
        DeleteCollectionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedMemberChanged(CollectionMemberViewModel? value) =>
        NavigateToSelectedCommand.NotifyCanExecuteChanged();

    public void RefreshCollections()
    {
        var selectedId = SelectedCollection?.Id;
        Collections.Clear();
        if (service is not null)
        {
            foreach (var collection in service.GetCollections())
            {
                var (count, _, _) = CountMembers(collection);
                Collections.Add(new CollectionListItemViewModel(
                    collection.Id, collection.Name, collection.Kind, count));
            }
        }

        SelectedCollection = Collections.FirstOrDefault(item => string.Equals(item.Id, selectedId, StringComparison.Ordinal))
            ?? Collections.FirstOrDefault();
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(ShowEmptyCollectionsState));
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
        var current = SelectedCollection;
        if (service is null || catalogRepository is null || current is null)
        {
            return;
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
        }
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

    [RelayCommand(CanExecute = nameof(CanManageSelectedCollection))]
    private void RenameCollection()
    {
        if (service is null || collectionRepository is null || SelectedCollection is null)
        {
            return;
        }

        var name = RenameTo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.CollectionNameRequired);
            return;
        }

        var current = SelectedCollection;
        if (string.Equals(current.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            service.RenameCollection(current.Id, name);
            undoManager?.RecordCollectionRenamed(collectionRepository, current.Id, current.Name, name);
            current.Name = name;
            StatusText = LocalizationService.Current.Format(TextKeys.CollectionRenamed, name);
            RefreshCollections();
            SelectCollection(current.Id);
            Changed();
        }
        catch (Exception exception) when (exception is CollectionNameRequiredException
            || exception is CollectionDuplicateNameException
            || exception is CollectionNotFoundException)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageSelectedCollection))]
    private void DeleteCollection()
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
            RefreshCollections();
            Changed();
        }
        catch (CollectionNotFoundException exception)
        {
            StatusText = exception.Message;
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

    [RelayCommand]
    private void RefreshResults() => RefreshCollections();

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

    partial void OnHasMissingMembersChanged(bool value) => RemoveMissingEntriesCommand.NotifyCanExecuteChanged();

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
