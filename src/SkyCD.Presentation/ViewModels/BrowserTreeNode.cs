using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class BrowserTreeNode : ObservableObject
{
    public BrowserTreeNode(string key, string title, string iconGlyph, IReadOnlyList<BrowserTreeNode> children, bool isExpanded = false)
    {
        Key = key;
        Title = title;
        IconGlyph = iconGlyph;
        Children = children;
        this.isExpanded = isExpanded;
    }

    public BrowserTreeNode(string key, string title, string iconGlyph)
        : this(key, title, iconGlyph, [], false)
    {
    }

    public string Key { get; }

    public string Title { get; }

    public string IconGlyph { get; }

    public IReadOnlyList<BrowserTreeNode> Children { get; }

    [ObservableProperty]
    private bool isExpanded;
}
