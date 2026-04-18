namespace SkyCD.Presentation.ViewModels;

public sealed record BrowserTreeNode(string Key, string Title, IReadOnlyList<BrowserTreeNode> Children)
{
    public BrowserTreeNode(string key, string title)
        : this(key, title, [])
    {
    }
}
