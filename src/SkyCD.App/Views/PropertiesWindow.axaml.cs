using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.Presentation.ViewModels;
using SkyCD.UI.Controls.Properties;

namespace SkyCD.App.Views;

public partial class PropertiesWindow : Window
{
    private readonly List<TabItem> detailsTabItems = [];
    private PropertiesDialogViewModel? subscribedViewModel;

    public PropertiesWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        CustomPropertiesList.ValueCellEditRequested += OnCustomPropertyEditRequested;
        CustomPropertiesList.RowSelectionChanged += OnCustomPropertyRowSelectionChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is not PropertiesWindow window || window.DataContext is not PropertiesDialogViewModel vm)
        {
            return;
        }

        if (ReferenceEquals(subscribedViewModel, vm))
        {
            return;
        }

        if (subscribedViewModel is not null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.CopyTextRequested -= OnCopyTextRequested;
        }

        subscribedViewModel = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        vm.CopyTextRequested += OnCopyTextRequested;
        SyncDetailsTabs(vm);
    }

    private void OnClosed(object? sender, EventArgs e) => (DataContext as IDisposable)?.Dispose();

    /// <summary>
    /// Appends the plugin-contributed tabs after the built-in ones and selects the tab a plugin command
    /// asked for. Tab content stays plugin-owned and is disposed with the dialog.
    /// </summary>
    private void SyncDetailsTabs(PropertiesDialogViewModel vm)
    {
        foreach (var item in detailsTabItems)
        {
            Tabs.Items.Remove(item);
        }

        detailsTabItems.Clear();
        foreach (var tab in vm.DetailsTabs)
        {
            var content = new ContentControl();
            content.Bind(ContentControl.ContentProperty, new Binding(nameof(PropertiesDetailsTabViewModel.Content))
            {
                Source = tab
            });
            var item = new TabItem { Header = tab.Title, Content = content };
            Tabs.Items.Add(item);
            detailsTabItems.Add(item);
        }

        if (vm.InitialDetailsTabKey is not { Length: > 0 } key)
        {
            return;
        }

        var index = 0;
        while (index < vm.DetailsTabs.Count &&
               !string.Equals(vm.DetailsTabs[index].Key, key, StringComparison.Ordinal))
        {
            index++;
        }

        if (index < detailsTabItems.Count)
        {
            Tabs.SelectedItem = detailsTabItems[index];
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is PropertiesDialogViewModel vm &&
            e.PropertyName == nameof(PropertiesDialogViewModel.DialogAccepted) &&
            vm.DialogAccepted)
        {
            CommitActiveCustomPropertyEdit();
            Close(true);
        }
    }

    private async void OnCopyTextRequested(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && !string.IsNullOrEmpty(text))
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    /// <summary>Moving the grid selection commits the editor the user left behind.</summary>
    private void OnCustomPropertyRowSelectionChanged(object? sender, EventArgs e) =>
        CommitActiveCustomPropertyEdit(exclude: CustomPropertiesList.SelectedItem as CustomPropertyItemViewModel);

    /// <summary>The grid asked to edit a row; only one cell edits at a time.</summary>
    private void OnCustomPropertyEditRequested(object? sender, PropertiesValueEditRequestedEventArgs e)
    {
        if (e.Item is CustomPropertyItemViewModel item)
        {
            BeginCustomPropertyEdit(item);
        }
    }

    /// <summary>
    /// Enters edit mode for <paramref name="item"/>. The committed <see cref="CustomPropertyItemViewModel.DefaultValue"/>
    /// is untouched until the edit commits, so Confirm always saves committed values.
    /// </summary>
    private void BeginCustomPropertyEdit(CustomPropertyItemViewModel item)
    {
        CommitActiveCustomPropertyEdit(exclude: item);
        item.BeginEdit();
        FocusCustomPropertyEditor(item);
    }

    private void CommitActiveCustomPropertyEdit(CustomPropertyItemViewModel? exclude = null)
    {
        foreach (var candidate in subscribedViewModel?.CustomProperties ?? [])
        {
            if (candidate is CustomPropertyItemViewModel { IsEditing: true } editing && !ReferenceEquals(editing, exclude))
            {
                editing.CommitEdit();
            }
        }
    }

    private void OnCustomPropertyValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { DataContext: CustomPropertyItemViewModel item })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                item.CancelEdit();
                e.Handled = true;
                // Mirror the classic grid: cancelling keeps the row selected and focused.
                FocusCustomPropertyRow(item);
                break;
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                item.CommitEdit();
                e.Handled = true;
                FocusCustomPropertyRow(item);
                break;
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                // Tabbing is navigation, not editing: commit, then open the next row's editor.
                item.CommitEdit();
                if (CustomPropertiesList.MoveEdit(item, 1))
                {
                    e.Handled = true;
                }

                break;
            case Key.Tab when e.KeyModifiers == KeyModifiers.Shift:
                item.CommitEdit();
                if (CustomPropertiesList.MoveEdit(item, -1))
                {
                    e.Handled = true;
                }

                break;
        }
    }

    private void FocusCustomPropertyEditor(CustomPropertyItemViewModel item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (CustomPropertiesList.FindValueEditor(item) is { } editor)
            {
                editor.Focus();
                editor.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void FocusCustomPropertyRow(CustomPropertyItemViewModel item)
    {
        Dispatcher.UIThread.Post(() => CustomPropertiesList.FocusRow(item, startEditing: false), DispatcherPriority.Input);
    }
}
