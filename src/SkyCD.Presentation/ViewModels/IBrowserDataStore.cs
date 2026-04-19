namespace SkyCD.Presentation.ViewModels;

public interface IBrowserDataStore
{
    IReadOnlyList<BrowserTreeNode> GetTreeNodes();

    IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey);
}
