using CommunityToolkit.Mvvm.ComponentModel;
using SkyCD.Documents;
using SkyCD.Documents.Enum;

namespace SkyCD.Presentation.ViewModels;

public sealed partial class CollectionListItemViewModel : ObservableObject
{
    public CollectionListItemViewModel(string id, string name, VirtualCollectionKind kind, int entryCount)
    {
        Id = id;
        Name = name;
        Kind = kind;
        EntryCount = entryCount;
    }

    public string Id { get; }

    [ObservableProperty]
    private string name = string.Empty;

    /// <summary>True while the row is being renamed inline on the Collections page.</summary>
    [ObservableProperty]
    private bool isRenaming;

    /// <summary>Draft name edited inline; committed with CommitRenameCollection.</summary>
    [ObservableProperty]
    private string renameDraft = string.Empty;

    public VirtualCollectionKind Kind { get; }

    [ObservableProperty]
    private int entryCount;

    /// <summary>Missing references for a manual collection; always zero for dynamic ones.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingEntryCount))]
    private int missingEntryCount;

    public bool HasMissingEntryCount => MissingEntryCount > 0;

    /// <summary>Localized per-row summary, for example entry and missing counts.</summary>
    [ObservableProperty]
    private string entrySummary = string.Empty;

    public bool IsDynamic => Kind == VirtualCollectionKind.Dynamic;
}
