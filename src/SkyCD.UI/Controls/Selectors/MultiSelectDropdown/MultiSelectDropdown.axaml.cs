using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

public partial class MultiSelectDropdown : UserControl
{
    public static readonly StyledProperty<IEnumerable<MultiSelectOptionItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, IEnumerable<MultiSelectOptionItem>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, string?>(nameof(PlaceholderText), "Select...");

    public static readonly StyledProperty<bool> IsSingleSelectProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(IsSingleSelect));

    public static readonly StyledProperty<bool> ShowIconSymbolsProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(ShowIconSymbols));

    private readonly ObservableCollection<MultiSelectOptionItem> filteredItems = [];
    private IEnumerable<MultiSelectOptionItem>? trackedItems;
    private bool suppressInlineTextProcessing;
    private string searchText = string.Empty;

    public MultiSelectDropdown()
    {
        InitializeComponent();
        ItemsListBox.ItemsSource = filteredItems;
    }

    public IEnumerable<MultiSelectOptionItem>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public bool IsSingleSelect
    {
        get => GetValue(IsSingleSelectProperty);
        set => SetValue(IsSingleSelectProperty, value);
    }

    public bool ShowIconSymbols
    {
        get => GetValue(ShowIconSymbolsProperty);
        set => SetValue(ShowIconSymbolsProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            RebindItems();
            return;
        }

        if (change.Property == PlaceholderTextProperty)
        {
            RefreshSelectedItemsView();
        }
    }

    private void RebindItems()
    {
        DetachItemHandlers(trackedItems);
        trackedItems = ItemsSource;
        AttachItemHandlers(trackedItems);
        RefreshFilteredItems();
        RefreshSelectedItemsView();
    }

    private void AttachItemHandlers(IEnumerable<MultiSelectOptionItem>? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            item.PropertyChanged += OnOptionItemPropertyChanged;
        }

        if (items is INotifyCollectionChanged observableCollection)
        {
            observableCollection.CollectionChanged += OnItemsCollectionChanged;
        }
    }

    private void DetachItemHandlers(IEnumerable<MultiSelectOptionItem>? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items)
        {
            item.PropertyChanged -= OnOptionItemPropertyChanged;
        }

        if (items is INotifyCollectionChanged observableCollection)
        {
            observableCollection.CollectionChanged -= OnItemsCollectionChanged;
        }
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var oldItem in e.OldItems.OfType<MultiSelectOptionItem>())
            {
                oldItem.PropertyChanged -= OnOptionItemPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var newItem in e.NewItems.OfType<MultiSelectOptionItem>())
            {
                newItem.PropertyChanged += OnOptionItemPropertyChanged;
            }
        }

        RefreshFilteredItems();
        RefreshSelectedItemsView();
    }

    private void OnOptionItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MultiSelectOptionItem.IsSelected) ||
            e.PropertyName == nameof(MultiSelectOptionItem.Label))
        {
            RefreshSelectedItemsView();
        }
    }

    private void OnToggleButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Not used anymore.
    }

    private void OnInlineSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (suppressInlineTextProcessing)
        {
            return;
        }

        searchText = InlineSearchTextBox.Text?.Trim() ?? string.Empty;
        if (InlineSearchTextBox.IsFocused && !DropdownPopup.IsOpen)
        {
            DropdownPopup.IsOpen = true;
        }

        RefreshFilteredItems();
    }

    private void OnInlineSearchTextBoxGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        suppressInlineTextProcessing = true;
        InlineSearchTextBox.Text = string.Empty;
        suppressInlineTextProcessing = false;
        searchText = string.Empty;
        DropdownPopup.IsOpen = true;
        RefreshFilteredItems();
    }

    private void OnChevronPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        DropdownPopup.IsOpen = !DropdownPopup.IsOpen;
        if (DropdownPopup.IsOpen)
        {
            InlineSearchTextBox.Focus();
        }

        e.Handled = true;
    }

    private void OnInlineSearchTextBoxLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!DropdownPopup.IsOpen)
        {
            searchText = string.Empty;
            RefreshSelectedItemsView();
        }
    }

    private void OnInlineSearchTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Down && e.Key != Key.Up && e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }

        if (!DropdownPopup.IsOpen)
        {
            DropdownPopup.IsOpen = true;
            RefreshFilteredItems();
        }

        if (filteredItems.Count == 0)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            var targetIndex = e.Key == Key.Up
                ? Math.Max(0, filteredItems.Count - 1)
                : 0;
            if (ItemsListBox.SelectedIndex < 0)
            {
                ItemsListBox.SelectedIndex = targetIndex;
            }
            else
            {
                MoveSelection(e.Key);
            }

            Dispatcher.UIThread.Post(() => ItemsListBox.Focus(), DispatcherPriority.Input);
            e.Handled = true;
            return;
        }

        ToggleCurrentItemFromKeyboard();
        e.Handled = true;
    }

    private void OnItemsListGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (ItemsListBox.SelectedIndex < 0 && filteredItems.Count > 0)
        {
            ItemsListBox.SelectedIndex = 0;
        }
    }

    private void RefreshFilteredItems()
    {
        filteredItems.Clear();
        var search = searchText;
        var items = ItemsSource ?? [];

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(search) ||
                item.Label.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                filteredItems.Add(item);
            }
        }
    }

    private void RefreshSelectedItemsView()
    {
        var selected = (ItemsSource ?? [])
            .Where(static item => item.IsSelected)
            .ToArray();

        var summary = selected.Length > 0
            ? string.Join(", ", selected.Select(static item => item.Label))
            : PlaceholderText ?? string.Empty;
        suppressInlineTextProcessing = true;
        InlineSearchTextBox.Text = summary;
        suppressInlineTextProcessing = false;
    }

    private void OnItemsListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is CheckBox)
        {
            return;
        }

        var element = e.Source as Control;
        while (element is not null && element is not ListBoxItem)
        {
            element = element.GetVisualParent() as Control;
        }

        if (element is ListBoxItem listBoxItem &&
            listBoxItem.DataContext is MultiSelectOptionItem item)
        {
            if (IsSingleSelect)
            {
                foreach (var candidate in ItemsSource ?? [])
                {
                    candidate.IsSelected = ReferenceEquals(candidate, item);
                }

                DropdownPopup.IsOpen = false;
                searchText = string.Empty;
                RefreshSelectedItemsView();
            }
            else
            {
                item.IsSelected = !item.IsSelected;
                RefreshSelectedItemsView();
            }

            ItemsListBox.SelectedItem = null;
            e.Handled = true;
        }
    }

    private void OnItemCheckBoxPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnItemsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!IsSingleSelect)
        {
            return;
        }

        if (IsSingleSelect)
        {
            foreach (var selected in e.AddedItems.OfType<MultiSelectOptionItem>())
            {
                foreach (var candidate in ItemsSource ?? [])
                {
                    candidate.IsSelected = ReferenceEquals(candidate, selected);
                }
            }

            DropdownPopup.IsOpen = false;
            searchText = string.Empty;
            RefreshSelectedItemsView();
            ItemsListBox.SelectedItem = null;
            return;
        }
    }

    private void OnItemsListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Up || e.Key == Key.Down)
        {
            MoveSelection(e.Key);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }

        ToggleCurrentItemFromKeyboard();
        e.Handled = true;
    }

    private void MoveSelection(Key directionKey)
    {
        if (filteredItems.Count == 0)
        {
            return;
        }

        var current = ItemsListBox.SelectedIndex;
        if (current < 0)
        {
            current = directionKey == Key.Up ? filteredItems.Count : -1;
        }

        var next = directionKey == Key.Down
            ? Math.Min(filteredItems.Count - 1, current + 1)
            : Math.Max(0, current - 1);

        ItemsListBox.SelectedIndex = next;
        var selectedItem = ItemsListBox.SelectedItem;
        if (selectedItem is not null)
        {
            ItemsListBox.ScrollIntoView(selectedItem);
        }
    }

    private void ToggleCurrentItemFromKeyboard()
    {
        if (ItemsListBox.SelectedItem is not MultiSelectOptionItem item)
        {
            return;
        }

        if (IsSingleSelect)
        {
            foreach (var candidate in ItemsSource ?? [])
            {
                candidate.IsSelected = ReferenceEquals(candidate, item);
            }

            DropdownPopup.IsOpen = false;
            searchText = string.Empty;
            RefreshSelectedItemsView();
            ItemsListBox.SelectedItem = null;
            return;
        }

        item.IsSelected = !item.IsSelected;
        RefreshSelectedItemsView();
    }
}
