using System.Collections.Generic;
using SkyCD.Documents.Collections;

namespace SkyCD.Presentation.ViewModels;

public interface IBrowserDataStore
{
    IReadOnlyList<BrowserTreeNode> GetTreeNodes();

    IReadOnlyList<BrowserItem> GetBrowserItems(string nodeKey);

    PropertiesCollection GetBrowserItemInfoProperties(string itemId);
}
