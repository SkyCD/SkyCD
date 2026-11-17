using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Presentation.Search;
using SkyCD.Presentation.Tags;
using SkyCD.Presentation.UndoRedo;
using SkyCD.UI.Localization;

namespace SkyCD.Presentation.ViewModels;

/// <summary>
/// Tag manager dialog: create, rename, merge, and delete reusable tags, and
/// find entries carrying a tag through the shared advanced-search contract.
/// Definition changes apply immediately and join the undo history; closing
/// the dialog changes nothing further.
/// </summary>
public sealed partial class TagsDialogViewModel : ObservableObject
{
    private readonly CatalogTagService? tagService;
    private readonly CatalogUndoManager? undoManager;
    private readonly CatalogDocumentRepository? catalogRepository;
    private readonly TagDocumentRepository? tagRepository;
    private readonly Action? onChanged;
    private readonly Action<CatalogDocument>? navigate;

    public TagsDialogViewModel(
        CatalogTagService? tagService,
        CatalogUndoManager? undoManager,
        CatalogDocumentRepository? catalogRepository,
        TagDocumentRepository? tagRepository,
        Action? onChanged = null,
        Action<CatalogDocument>? navigate = null)
    {
        this.tagService = tagService;
        this.undoManager = undoManager;
        this.catalogRepository = catalogRepository;
        this.tagRepository = tagRepository;
        this.onChanged = onChanged;
        this.navigate = navigate;
        RefreshTags();
    }

    public ObservableCollection<TagAssignmentViewModel> Tags { get; } = [];

    public ObservableCollection<CatalogFindResult> FilterResults { get; } = [];

    [ObservableProperty]
    private TagAssignmentViewModel? selectedTag;

    [ObservableProperty]
    private string newTagName = string.Empty;

    [ObservableProperty]
    private string renameTo = string.Empty;

    [ObservableProperty]
    private TagAssignmentViewModel? mergeTarget;

    [ObservableProperty]
    private CatalogFindResult? selectedResult;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private bool dialogAccepted;

    public bool TagsAvailable => tagService is not null;

    partial void OnSelectedTagChanged(TagAssignmentViewModel? value)
    {
        if (value is not null)
        {
            RenameTo = value.Name;
        }

        FilterResults.Clear();
        ShowItemsCommand.NotifyCanExecuteChanged();
    }

    public void RefreshTags()
    {
        var selectedId = SelectedTag?.TagId;
        Tags.Clear();
        if (tagService is null)
        {
            return;
        }

        var usage = CountUsages(tagService);
        foreach (var tag in tagService.GetTags())
        {
            usage.TryGetValue(tag.Id, out var count);
            Tags.Add(new TagAssignmentViewModel(tag.Id, tag.Name, count));
        }

        SelectedTag = Tags.FirstOrDefault(tag => string.Equals(tag.TagId, selectedId, StringComparison.Ordinal))
            ?? Tags.FirstOrDefault();
    }

    private Dictionary<string, int> CountUsages(CatalogTagService service)
    {
        var usage = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var tag in service.GetTags())
        {
            usage[tag.Id] = 0;
        }

        if (catalogRepository is null)
        {
            return usage;
        }

        foreach (var entry in catalogRepository.GetAll())
        {
            foreach (var tagId in service.GetEntryTagIds(entry.Id))
            {
                if (usage.ContainsKey(tagId))
                {
                    usage[tagId]++;
                }
            }
        }

        return usage;
    }

    private bool CanManageTags() => tagService is not null && tagRepository is not null;

    [RelayCommand(CanExecute = nameof(CanManageTags))]
    private void CreateTag()
    {
        if (tagService is null || tagRepository is null || catalogRepository is null)
        {
            return;
        }

        var name = NewTagName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.TagNameRequired);
            return;
        }

        try
        {
            var tag = tagService.CreateTag(name);
            undoManager?.RecordTagCreated(tagRepository, catalogRepository, tag.Id, tag.Name);
            NewTagName = string.Empty;
            StatusText = LocalizationService.Current.Format(TextKeys.TagCreated, tag.Name);
            RefreshTags();
            SelectTag(tag.Id);
            onChanged?.Invoke();
        }
        catch (Exception exception) when (exception is TagDuplicateNameException || exception is TagNameRequiredException)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageTags))]
    private void RenameTag()
    {
        if (tagService is null || tagRepository is null || SelectedTag is null)
        {
            return;
        }

        var name = RenameTo?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusText = LocalizationService.Current.Get(TextKeys.TagNameRequired);
            return;
        }

        var oldName = SelectedTag.Name;
        if (string.Equals(oldName, name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            tagService.RenameTag(SelectedTag.TagId, name);
            undoManager?.RecordTagRenamed(tagRepository, SelectedTag.TagId, oldName, name);
            SelectedTag.Name = name;
            StatusText = LocalizationService.Current.Format(TextKeys.TagRenamed, name);
            RefreshTags();
            SelectTag(SelectedTag.TagId);
            onChanged?.Invoke();
        }
        catch (Exception exception) when (exception is TagDuplicateNameException
            || exception is TagNameRequiredException
            || exception is TagNotFoundException)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageTags))]
    private void DeleteTag()
    {
        if (tagService is null || tagRepository is null || catalogRepository is null || SelectedTag is null)
        {
            return;
        }

        var tagId = SelectedTag.TagId;
        var tagName = SelectedTag.Name;
        var affected = CollectAffected(tagService, tagId);
        try
        {
            tagService.DeleteTag(tagId);
            undoManager?.RecordTagDeleted(catalogRepository, tagRepository, tagId, tagName, affected);
            StatusText = LocalizationService.Current.Format(TextKeys.TagDeleted, tagName);
            RefreshTags();
            onChanged?.Invoke();
        }
        catch (TagNotFoundException exception)
        {
            StatusText = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageTags))]
    private void MergeTags()
    {
        if (tagService is null || tagRepository is null || catalogRepository is null || SelectedTag is null)
        {
            return;
        }

        var source = SelectedTag;
        var target = MergeTarget;
        if (target is null || string.Equals(source.TagId, target.TagId, StringComparison.Ordinal))
        {
            return;
        }

        var affected = CollectAffected(tagService, source.TagId);
        try
        {
            tagService.MergeTags(source.TagId, target.TagId);
            undoManager?.RecordTagsMerged(catalogRepository, tagRepository, source.TagId, source.Name, target.TagId, affected);
            StatusText = LocalizationService.Current.Format(TextKeys.TagMerged, source.Name, target.Name);
            MergeTarget = null;
            RefreshTags();
            SelectTag(target.TagId);
            onChanged?.Invoke();
        }
        catch (TagNotFoundException exception)
        {
            StatusText = exception.Message;
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> CollectAffected(
        CatalogTagService service, string tagId)
    {
        var affected = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var entry in service.GetEntriesWithTag(tagId))
        {
            affected[entry.Id] = service.GetEntryTagIds(entry.Id);
        }

        return affected;
    }

    private void SelectTag(string tagId)
    {
        SelectedTag = Tags.FirstOrDefault(tag => string.Equals(tag.TagId, tagId, StringComparison.Ordinal));
    }

    private bool CanShowItems() => tagService is not null && SelectedTag is not null;

    [RelayCommand(CanExecute = nameof(CanShowItems))]
    private void ShowItems()
    {
        FilterResults.Clear();
        if (tagService is null || catalogRepository is null || SelectedTag is null)
        {
            return;
        }

        var query = new AdvancedCatalogQuery
        {
            Root = new CatalogQueryGroup
            {
                Combination = CatalogQueryCombination.All,
                Predicates =
                [
                    new AdvancedCatalogPredicate
                    {
                        Field = AdvancedCatalogField.CustomProperty,
                        Operator = AdvancedCatalogOperator.Contains,
                        Key = CatalogTagService.TagsPropertyKey,
                        Value = SelectedTag.TagId
                    }
                ]
            },
            Take = AdvancedCatalogSearchService.MaxTake
        };

        var index = new CatalogSearchIndex();
        index.Rebuild(catalogRepository.GetAll());
        var matches = AdvancedCatalogSearchService.Search(index, query);
        foreach (var match in matches)
        {
            FilterResults.Add(match);
        }

        StatusText = LocalizationService.Current.Format(TextKeys.TagFilterMatchCount, matches.Count, SelectedTag.Name);
    }

    private bool CanNavigate() => navigate is not null && SelectedResult is not null;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void NavigateToSelected()
    {
        if (navigate is not null && SelectedResult is not null)
        {
            navigate(SelectedResult.Document);
        }
    }

    partial void OnSelectedResultChanged(CatalogFindResult? value) => NavigateToSelectedCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void Confirm()
    {
        DialogAccepted = true;
    }
}
