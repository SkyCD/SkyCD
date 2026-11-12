using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using SkyCD.App.Views;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class ParentFolderNavigationTests
{
    [Fact]
    public void NavigateToParentFolder_NavigatesToDirectParentInDeepTree()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var musicNode = vm.TreeNodes[0].Children.Single(node => node.Key == "music");
        var classicalNode = musicNode.Children.Single(node => node.Key == "classical-collection");

        vm.SelectedTreeNode = classicalNode;
        Assert.Equal("classical-collection", vm.SelectedTreeNode?.Key);
        Assert.True(vm.NavigateToParentFolderCommand.CanExecute(null));

        vm.NavigateToParentFolderCommand.Execute(null);

        Assert.Equal("music", vm.SelectedTreeNode?.Key);
        Assert.Equal("Navigated to Music.", vm.StatusText);

        vm.NavigateToParentFolderCommand.Execute(null);

        Assert.Equal("library", vm.SelectedTreeNode?.Key);
        Assert.Equal("Navigated to Library.", vm.StatusText);
        Assert.False(vm.NavigateToParentFolderCommand.CanExecute(null));
    }

    [Fact]
    public void NavigateToParentFolder_WhenAtRoot_CommandCannotExecute()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        Assert.Equal("library", vm.SelectedTreeNode?.Key);
        Assert.False(vm.NavigateToParentFolderCommand.CanExecute(null));

        vm.NavigateToParentFolderCommand.Execute(null);

        Assert.Equal("library", vm.SelectedTreeNode?.Key);
    }

    [Fact]
    public void NavigateToParentFolder_CommandCanExecuteTracksSelectedNode()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        Assert.False(vm.NavigateToParentFolderCommand.CanExecute(null));

        var moviesNode = vm.TreeNodes[0].Children.Single(node => node.Key == "movies");
        vm.SelectedTreeNode = moviesNode;

        Assert.True(vm.NavigateToParentFolderCommand.CanExecute(null));

        vm.SelectedTreeNode = vm.TreeNodes[0];

        Assert.False(vm.NavigateToParentFolderCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(Key.Back)]
    [InlineData(Key.BrowserBack)]
    public void MainWindow_OnMainWindowKeyDown_NavigatesToParent(Key key)
    {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        typeof(MainWindow).GetField("subscribedViewModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, vm);

        var musicNode = vm.TreeNodes[0].Children.Single(node => node.Key == "music");
        vm.SelectedTreeNode = musicNode;

        var onKeyDownMethod = typeof(MainWindow).GetMethod("OnMainWindowKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var keyEventArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.None,
            Source = null
        };

        onKeyDownMethod.Invoke(window, [window, keyEventArgs]);

        Assert.True(keyEventArgs.Handled);
        Assert.Equal("library", vm.SelectedTreeNode?.Key);
    }

    [Fact]
    public void MainWindow_OnMainWindowKeyDown_Backspace_Ignored_WhenSourceIsTextBox()
    {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        typeof(MainWindow).GetField("subscribedViewModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, vm);

        var musicNode = vm.TreeNodes[0].Children.Single(node => node.Key == "music");
        vm.SelectedTreeNode = musicNode;

        var textBox = new TextBox();
        var onKeyDownMethod = typeof(MainWindow).GetMethod("OnMainWindowKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var keyEventArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Back,
            KeyModifiers = KeyModifiers.None,
            Source = textBox
        };

        onKeyDownMethod.Invoke(window, [window, keyEventArgs]);

        Assert.False(keyEventArgs.Handled);
        Assert.Equal("music", vm.SelectedTreeNode?.Key);
    }

    [Fact]
    public void MainWindow_OnMainWindowKeyDown_Backspace_AtRoot_DoesNotHandle()
    {
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        typeof(MainWindow).GetField("subscribedViewModel", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(window, vm);

        Assert.Equal("library", vm.SelectedTreeNode?.Key);

        var onKeyDownMethod = typeof(MainWindow).GetMethod("OnMainWindowKeyDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var keyEventArgs = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Back,
            KeyModifiers = KeyModifiers.None,
            Source = null
        };

        onKeyDownMethod.Invoke(window, [window, keyEventArgs]);

        Assert.False(keyEventArgs.Handled);
        Assert.Equal("library", vm.SelectedTreeNode?.Key);
    }
}
