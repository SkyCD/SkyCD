namespace SkyCD.Presentation.ViewModels;

public sealed record BrowserTreeNode(
    string Key,
    string Title,
    string IconGlyph,
    IReadOnlyList<BrowserTreeNode> Children)
{
    public BrowserTreeNode(string key, string title, string iconGlyph)
        : this(key, title, iconGlyph, [])
    {
    }
}
