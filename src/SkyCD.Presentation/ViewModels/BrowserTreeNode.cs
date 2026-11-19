using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SkyCD.Presentation.ViewModels;

public partial class BrowserTreeNode : ObservableObject
{
    public BrowserTreeNode(string key, string title, string iconGlyph, IReadOnlyList<BrowserTreeNode> children,
        bool isExpanded = false)
    {
        Key = key;
        Title = title;
        IconGlyph = iconGlyph;
        Children = new ObservableCollection<BrowserTreeNode>(children);
        this.isExpanded = isExpanded;
    }

    public BrowserTreeNode(string key, string title, string iconGlyph)
        : this(key, title, iconGlyph, [], false)
    {
    }

    public string Key { get; }

    public string Title { get; }

    public string IconGlyph { get; }

    public string? LoanIndicator { get; init; }

    // Observable so a deleted node can be detached in place. Rebuilding the whole tree instead
    // destroys every realized TreeViewItem, including the one holding keyboard focus.
    public ObservableCollection<BrowserTreeNode> Children { get; }

    [ObservableProperty] private bool isExpanded;
}