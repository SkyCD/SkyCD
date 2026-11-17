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

    public VirtualCollectionKind Kind { get; }

    [ObservableProperty]
    private int entryCount;

    public bool IsDynamic => Kind == VirtualCollectionKind.Dynamic;
}
