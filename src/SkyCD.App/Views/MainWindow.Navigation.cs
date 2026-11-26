using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Lists;
using SkyCD.UI.Controls.Tree;

namespace SkyCD.App.Views;

public partial class MainWindow
{
    private void OnFocusBrowserRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");
            if (browserView is not null)
            {
                browserView.FocusActiveControl();
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Browser;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnFocusTreeRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var treeView = this.FindControl<TreeView>("MainTreeView");
            if (treeView is null || subscribedViewModel is null)
            {
                return;
            }

            TreeViewFocus.FocusItem(treeView, subscribedViewModel.SelectedTreeNode);
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }, DispatcherPriority.Loaded);
    }

    private void OnRenameRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (subscribedViewModel?.EditingBrowserItemId is not null)
            {
                FocusBrowserRenameEditor(attempts: 3);
                return;
            }

            var treeView = this.FindControl<TreeView>("MainTreeView");
            if (treeView is null || subscribedViewModel?.SelectedTreeNode is not { IsRenaming: true } node)
            {
                return;
            }

            var container = TreeViewFocus.FindContainer(treeView, node) as TreeViewItem;
            var editor = container?.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(static textBox => textBox.IsVisible);
            if (editor is null)
            {
                return;
            }

            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    // The row may not be realized yet when the edit starts, so retry until virtualization has
    // produced the editor.
    private void FocusBrowserRenameEditor(int attempts)
    {
        var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");
        if (browserView?.FocusRenameEditor() == true || attempts <= 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => FocusBrowserRenameEditor(attempts - 1), DispatcherPriority.Background);
    }

    private void OnBrowserRenameCommitted(object? sender, EventArgs e)
    {
        subscribedViewModel?.CommitBrowserItemRename();
    }

    private void OnBrowserRenameCancelled(object? sender, EventArgs e)
    {
        subscribedViewModel?.CancelBrowserItemRename();
    }

    private void OnBrowserRenameAbandoned(object? sender, EventArgs e)
    {
        subscribedViewModel?.CancelBrowserItemRename(restoreFocus: false);
    }

    private void OnRenameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (subscribedViewModel is null || sender is not TextBox { DataContext: BrowserTreeNode node })
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            subscribedViewModel.CommitRename(node);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            subscribedViewModel.CancelRename(node);
        }
    }

    private void OnRenameEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (subscribedViewModel is null || sender is not TextBox { DataContext: BrowserTreeNode node } || !node.IsRenaming)
        {
            return;
        }

        subscribedViewModel.CancelRename(node, restoreFocus: false);
    }

    private void OnTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TreeView treeView || subscribedViewModel is null)
        {
            return;
        }

        if (!e.TryGetPosition(treeView, out var point))
        {
            e.Handled = subscribedViewModel.SelectedTreeNode is null;
            return;
        }

        var hit = treeView.InputHitTest(point) as Visual;
        var treeViewItem = FindAncestor<TreeViewItem>(hit);
        if (treeViewItem?.DataContext is BrowserTreeNode node)
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
            subscribedViewModel.SelectedTreeNode = node;
            return;
        }

        e.Handled = true;
    }

    private void AttachPaneFocusHandlers()
    {
        var treeView = this.FindControl<TreeView>("MainTreeView");
        if (treeView is not null)
        {
            treeView.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Tree;
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            treeView.AddHandler(InputElement.GotFocusEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Tree;
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }

        var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");
        if (browserView is not null)
        {
            browserView.AddHandler(InputElement.PointerPressedEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Browser;
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);

            browserView.AddHandler(InputElement.GotFocusEvent, (_, _) =>
            {
                if (subscribedViewModel is not null)
                {
                    subscribedViewModel.ActivePane = ActivePane.Browser;
                }
            }, RoutingStrategies.Bubble, handledEventsToo: true);
        }
    }

    private void OnMainWindowKeyDown(object? sender, KeyEventArgs e) => KeyboardRouter.Route(this, e);

    private void OnFindCatalogRequested(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (subscribedViewModel?.IsQuickFilterOpen != true) return;
            QuickFindBox.Focus();
            QuickFindBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void OnQuickFindResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.Source as Visual) is not null)
        {
            OpenQuickFindResult();
        }
    }

    private void OnQuickFindResultKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        OpenQuickFindResult();
        e.Handled = true;
    }

    private void OpenQuickFindResult()
    {
        if (subscribedViewModel is not { SelectedQuickFindResult: not null } vm) return;
        vm.OpenQuickFindResultCommand.Execute(null);
        MainBrowserItemsView.FocusActiveControl();
    }

    private void OnAdvancedFindRequested(object? sender, AdvancedFindDialogRequestedEventArgs e) =>
        _ = RunUiActionAsync(() => AdvancedFindRequestedAsync(e));

    private async Task AdvancedFindRequestedAsync(AdvancedFindDialogRequestedEventArgs e)
    {
        await new AdvancedCatalogFindWindow { DataContext = e.Dialog }.ShowDialog<bool?>(this);
    }

    private IInputElement? GetFocusedElement()
    {
        try
        {
            return TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        }
        catch
        {
            return null;
        }
    }

    private void UpdateActivePaneFromFocusedElement(IInputElement? focused)
    {
        if (focused is not Visual visual || subscribedViewModel is null)
        {
            return;
        }

        var treeView = this.FindControl<TreeView>("MainTreeView");
        var browserView = this.FindControl<BrowserItemsView>("MainBrowserItemsView");

        if (treeView is not null && (visual == treeView || treeView.IsVisualAncestorOf(visual)))
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }
        else if (browserView is not null && (visual == browserView || browserView.IsVisualAncestorOf(visual)))
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
        }
    }

    private void OnTreeGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Tree;
        }
    }

    private void OnBrowserContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control control || subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.ActivePane = ActivePane.Browser;

        if (!e.TryGetPosition(control, out var point))
        {
            e.Handled = subscribedViewModel.SelectedBrowserItem is null;
            return;
        }

        var hit = control.InputHitTest(point) as Visual;
        var listBoxItem = FindAncestor<ListBoxItem>(hit);
        if (listBoxItem?.DataContext is CatalogDocument item)
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
            var currentSelected = subscribedViewModel.SelectedBrowserItems.OfType<CatalogDocument>().ToList();
            if (!currentSelected.Any(sel => string.Equals(sel.Id, item.Id, StringComparison.Ordinal)))
            {
                subscribedViewModel.SelectedBrowserItem = item;
                subscribedViewModel.SelectedBrowserItems = new List<CatalogDocument> { item };
            }
            return;
        }

        e.Handled = true;
    }

    private void OnBrowserGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
        }
    }

    private void OnBrowserPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (subscribedViewModel is not null)
        {
            subscribedViewModel.ActivePane = ActivePane.Browser;
        }
    }

    private void OnBrowserListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (subscribedViewModel is null)
        {
            return;
        }

        subscribedViewModel.NavigateToFolderCommand.Execute(null);
    }

    private static T? FindAncestor<T>(Visual? visual) where T : class
    {
        var current = visual;
        while (current is not null)
        {
            if (current is T target)
            {
                return target;
            }

            current = current.GetVisualParent();
        }

        return null;
    }

    private static class KeyboardRouter
    {
        public static void Route(MainWindow window, KeyEventArgs e)
        {
            try
            {
                if (window.OwnedWindows is { Count: > 0 } && window.OwnedWindows.Any(static w => w?.IsVisible == true))
                {
                    return;
                }
            }
            catch
            {
                // In headless or unit test environments OwnedWindows may not be initialized
            }

            if (window.subscribedViewModel is { IsQuickFilterOpen: true } vm && e.Key == Key.Escape)
            {
                vm.CloseQuickFilterCommand.Execute(null);
                window.MainBrowserItemsView.FocusActiveControl();
                e.Handled = true;
                return;
            }

            if (window.subscribedViewModel?.ShowQuickFindResults == true &&
                e.Key == Key.Down && e.KeyModifiers == KeyModifiers.None && window.GetFocusedElement() == window.QuickFindBox)
            {
                window.QuickFindResultList.Focus();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && e.Source is Visual source &&
                (source == window.QuickFindResultList || window.QuickFindResultList.IsVisualAncestorOf(source)))
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete && e.KeyModifiers == KeyModifiers.None)
            {
                var focused = window.GetFocusedElement();
                if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                    e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
                {
                    return;
                }

                if (window.subscribedViewModel is not null)
                {
                    window.UpdateActivePaneFromFocusedElement(focused);

                    if (window.subscribedViewModel.DeleteItemCommand.CanExecute(null))
                    {
                        window.subscribedViewModel.DeleteItemCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
            else if ((e.Key == Key.Back || e.Key == Key.BrowserBack) && e.KeyModifiers == KeyModifiers.None)
            {
                var focused = window.GetFocusedElement();
                if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                    e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
                {
                    return;
                }

                if (window.subscribedViewModel is not null)
                {
                    window.UpdateActivePaneFromFocusedElement(focused);

                    if (window.subscribedViewModel.NavigateToParentFolderCommand.CanExecute(null))
                    {
                        window.subscribedViewModel.NavigateToParentFolderCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.Z && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control))
            {
                var focused = window.GetFocusedElement();
                if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                    e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
                {
                    return;
                }

                if (window.subscribedViewModel?.UndoCommand.CanExecute(null) == true)
                {
                    window.subscribedViewModel.UndoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if ((e.Key == Key.Y && e.KeyModifiers == KeyModifiers.Control && !OperatingSystem.IsMacOS())
                || (e.Key == Key.Z && e.KeyModifiers == ((OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | KeyModifiers.Shift)))
            {
                var focused = window.GetFocusedElement();
                if (focused is TextBox || FindAncestor<TextBox>(focused as Visual) is not null ||
                    e.Source is TextBox || FindAncestor<TextBox>(e.Source as Visual) is not null)
                {
                    return;
                }

                if (window.subscribedViewModel?.RedoCommand.CanExecute(null) == true)
                {
                    window.subscribedViewModel.RedoCommand.Execute(null);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
            {
                if (window.subscribedViewModel?.FindCatalogCommand.CanExecute(null) == true)
                {
                    window.subscribedViewModel.FindCatalogCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
