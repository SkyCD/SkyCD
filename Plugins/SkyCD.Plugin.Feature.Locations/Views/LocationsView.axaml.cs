using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkyCD.Plugin.Feature.Locations.Models;

namespace SkyCD.Plugin.Feature.Locations.Views;

public partial class LocationsView : UserControl
{
    private LocationsManagerViewModel? boundViewModel;

    public LocationsView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (ReferenceEquals(boundViewModel, DataContext))
        {
            return;
        }

        if (boundViewModel is not null)
        {
            boundViewModel.MergeLocationsRequested -= OnMergeLocationsRequested;
        }

        boundViewModel = DataContext as LocationsManagerViewModel;
        if (boundViewModel is not null)
        {
            boundViewModel.MergeLocationsRequested += OnMergeLocationsRequested;
        }
    }

    private void OnLocationsListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (boundViewModel is not { } viewModel ||
            FindLocationRow(e.Source as Visual) is not { } row)
        {
            return;
        }

        viewModel.SelectedLocation = row;
        viewModel.BeginLocationEdit(row);
        FocusLocationEditor(row);
        e.Handled = true;
    }

    private void OnLocationsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (boundViewModel is not { } viewModel || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        if (e.Key is Key.F2 or Key.Enter)
        {
            if (viewModel.SelectedLocation is { IsEditing: false } row)
            {
                viewModel.BeginLocationEdit(row);
                FocusLocationEditor(row);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Escape)
        {
            if (viewModel.Locations.FirstOrDefault(location => location.IsEditing) is { } editing)
            {
                viewModel.CancelLocationRename(editing);
                e.Handled = true;
            }
        }
    }

    private void OnLocationNameEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (boundViewModel is not { } viewModel ||
            sender is not TextBox { DataContext: LocationRow row })
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                if (viewModel.CommitLocationRename(row))
                {
                    FocusLocationsList();
                }

                break;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                viewModel.CancelLocationRename(row);
                FocusLocationsList();
                break;
            case Key.Tab when e.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift:
                // Leaving the editor commits; an invalid name keeps focus in the still-open editor.
                if (!viewModel.CommitLocationRename(row))
                {
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnLocationsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (boundViewModel is not { } viewModel)
        {
            return;
        }

        var selected = viewModel.SelectedLocation;
        foreach (var row in viewModel.Locations
                     .Where(location => location.IsEditing && !ReferenceEquals(location, selected))
                     .ToArray())
        {
            if (!viewModel.CommitLocationRename(row))
            {
                break;
            }
        }
    }

    private async void OnMergeLocationsRequested(object? sender, EventArgs e)
    {
        if (boundViewModel is not { SelectedLocation: { } source } viewModel)
        {
            return;
        }

        // Settle open editors first so the dialog starts from committed names.
        foreach (var row in viewModel.Locations.Where(location => location.IsEditing).ToArray())
        {
            if (!viewModel.CommitLocationRename(row))
            {
                return;
            }
        }

        var destinations = viewModel.GetMergeDestinations(source);
        if (destinations.Count == 0 || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        var dialog = new MergeLocationsWindow(new MergeLocationsDialogViewModel(source, destinations));
        if (await dialog.ShowDialog<LocationRow?>(owner) is { } target)
        {
            viewModel.ConfirmMergeLocations(source, target);
        }

        FocusLocationsList();
    }

    private void FocusLocationEditor(LocationRow row)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (LocationsList.ContainerFromItem(row) is not Control container)
            {
                return;
            }

            var editor = container.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(static textBox => textBox.IsVisible);
            if (editor is null)
            {
                return;
            }

            editor.Focus();
            editor.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void FocusLocationsList() =>
        Dispatcher.UIThread.Post(() => LocationsList.Focus(), DispatcherPriority.Input);

    private static LocationRow? FindLocationRow(Visual? source)
    {
        for (var visual = source; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual.DataContext is LocationRow row)
            {
                return row;
            }
        }

        return null;
    }
}
