using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace SkyCD.UI.Controls.Tree;

public static class TreeViewFocus
{
    /// <summary>
    /// Moves keyboard focus to the container of <paramref name="item"/>, falling back to the
    /// <see cref="TreeView"/> itself when that container is not realized.
    /// </summary>
    /// <returns>true when focus landed on the item's own container.</returns>
    public static bool FocusItem(TreeView treeView, object? item)
    {
        var container = FindContainer(treeView, item);
        if (container is null)
        {
            return treeView.Focus();
        }

        container.BringIntoView();
        return container.Focus();
    }

    /// <remarks>
    /// TreeView.ContainerFromItem only resolves top level items, so nested containers have to be
    /// found among the realized visual tree.
    /// </remarks>
    public static TreeViewItem? FindContainer(TreeView treeView, object? item)
    {
        if (item is null)
        {
            return null;
        }

        return treeView.GetSelfAndVisualDescendants()
            .OfType<TreeViewItem>()
            .FirstOrDefault(container => ReferenceEquals(container.DataContext, item));
    }
}
