using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Data;
using SkyCD.UI.Localization;

namespace SkyCD.UI.Controls.Selectors.MultiSelectDropdown;

public partial class MultiSelectDropdown : UserControl
{
    public static readonly StyledProperty<IEnumerable<MultiSelectOptionItem>?> ItemsSourceProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, IEnumerable<MultiSelectOptionItem>?>(nameof(ItemsSource));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, string?>(nameof(PlaceholderText));

    public static readonly StyledProperty<bool> IsSingleSelectProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(IsSingleSelect));

    public static readonly StyledProperty<IDataTemplate?> ItemPreviewTemplateProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, IDataTemplate?>(nameof(ItemPreviewTemplate));

    public static readonly StyledProperty<bool> ShowItemLabelsProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(ShowItemLabels), defaultValue: true);

    public static readonly StyledProperty<bool> IsIconPickerProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(IsIconPicker));

    public static readonly StyledProperty<IReadOnlyList<string>?> IconKeysProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, IReadOnlyList<string>?>(nameof(IconKeys));

    public static readonly StyledProperty<string?> SelectedIconKeyProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, string?>(nameof(SelectedIconKey), defaultBindingMode: BindingMode.TwoWay);

    private readonly ObservableCollection<MultiSelectOptionItem> filteredItems = [];
    private IEnumerable<MultiSelectOptionItem>? trackedItems;
    private bool suppressInlineTextProcessing;
    private string searchText = string.Empty;

    public MultiSelectDropdown()
    {
        InitializeComponent();
        this.Bind(PlaceholderTextProperty, new Binding($"[{ExtraKeys.Select}]")
        {
            Source = LocalizationService.Current,
            Priority = BindingPriority.Style
        });
        ItemsListBox.ItemsSource = filteredItems;
        IconItemsListBox.AddHandler(InputElement.PointerPressedEvent, OnIconItemsListPointerPressed, RoutingStrategies.Tunnel);
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

    public IDataTemplate? ItemPreviewTemplate
    {
        get => GetValue(ItemPreviewTemplateProperty);
        set => SetValue(ItemPreviewTemplateProperty, value);
    }

    public bool ShowItemLabels
    {
        get => GetValue(ShowItemLabelsProperty);
        set => SetValue(ShowItemLabelsProperty, value);
    }

    public bool IsIconPicker
    {
        get => GetValue(IsIconPickerProperty);
        set => SetValue(IsIconPickerProperty, value);
    }

    public IReadOnlyList<string>? IconKeys
    {
        get => GetValue(IconKeysProperty);
        set => SetValue(IconKeysProperty, value);
    }

    public string? SelectedIconKey
    {
        get => GetValue(SelectedIconKeyProperty);
        set => SetValue(SelectedIconKeyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            RebindItems();
            return;
        }

        if (change.Property == PlaceholderTextProperty || change.Property == SelectedIconKeyProperty)
        {
            RefreshSelectedItemsView();
            return;
        }

        if (change.Property == ItemPreviewTemplateProperty)
        {
            UpdateSelectedIconPreview();
            return;
        }

        if (change.Property == IconKeysProperty || change.Property == IsIconPickerProperty)
        {
            RefreshFilteredItems();
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
        if (IsIconPicker && !DropdownPopup.IsOpen &&
            (string.Equals(searchText, SelectedIconKey ?? PlaceholderText ?? string.Empty, StringComparison.Ordinal) ||
             (string.IsNullOrEmpty(searchText) && !string.IsNullOrWhiteSpace(SelectedIconKey))))
        {
            return;
        }

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
        UpdateSelectedIconPreview();
        RefreshFilteredItems();
    }

    private void OnChevronPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        DropdownPopup.IsOpen = !DropdownPopup.IsOpen;
        if (DropdownPopup.IsOpen)
        {
            InlineSearchTextBox.Focus();
            RefreshFilteredItems();
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

        var list = IsIconPicker ? IconItemsListBox : ItemsListBox;
        if (list.ItemCount == 0)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            var targetIndex = e.Key == Key.Up
                ? Math.Max(0, list.ItemCount - 1)
                : 0;
            if (list.SelectedIndex < 0)
            {
                list.SelectedIndex = targetIndex;
            }
            else
            {
                MoveSelection(e.Key);
            }

            Dispatcher.UIThread.Post(() => list.Focus(), DispatcherPriority.Input);
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

    private void OnIconItemsListGotFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IconItemsListBox.SelectedIndex < 0 && IconItemsListBox.ItemCount > 0)
        {
            IconItemsListBox.SelectedIndex = 0;
        }
    }

    private void RefreshFilteredItems()
    {
        if (IsIconPicker)
        {
            if (!DropdownPopup.IsOpen)
            {
                return;
            }

            var keys = IconKeys ?? [];
            IconItemsListBox.ItemsSource = string.IsNullOrWhiteSpace(searchText)
                ? keys
                : keys.Where(key => key.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToArray();
            IconItemsListBox.SelectedItem = SelectedIconKey;
            return;
        }

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
        if (IsIconPicker)
        {
            var hasIcon = !string.IsNullOrWhiteSpace(SelectedIconKey);
            var iconSummary = hasIcon
                ? string.Empty
                : PlaceholderText ?? string.Empty;
            suppressInlineTextProcessing = true;
            InlineSearchTextBox.Text = iconSummary;
            suppressInlineTextProcessing = false;
            AutomationProperties.SetName(InlineSearchTextBox, hasIcon ? SelectedIconKey! : iconSummary);
            UpdateSelectedIconPreview();
            return;
        }
        var selected = (ItemsSource ?? [])
            .Where(static item => item.IsSelected)
            .ToArray();

        var summary = selected.Length > 0
            ? string.Join(", ", selected.Select(static item => item.Label))
            : PlaceholderText ?? string.Empty;
        suppressInlineTextProcessing = true;
        InlineSearchTextBox.Text = summary;
        suppressInlineTextProcessing = false;
        UpdateSelectedIconPreview();
    }

    private void UpdateSelectedIconPreview()
    {
        SelectedIconPreview.IsVisible = IsIconPicker &&
            ItemPreviewTemplate is not null &&
            !string.IsNullOrWhiteSpace(SelectedIconKey) &&
            !DropdownPopup.IsOpen;
    }

    private void OnDropdownPopupClosed(object? sender, EventArgs e)
    {
        searchText = string.Empty;
        RefreshSelectedItemsView();
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

    private void OnIconItemsListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var element = e.Source as Control;
        while (element is not null && element is not ListBoxItem)
        {
            element = element.GetVisualParent() as Control;
        }

        if (element is ListBoxItem { DataContext: string key })
        {
            SelectIcon(key);
            e.Handled = true;
        }
    }

    private void SelectIcon(string key)
    {
        SetCurrentValue(SelectedIconKeyProperty, key);
        DropdownPopup.IsOpen = false;
        searchText = string.Empty;
        RefreshSelectedItemsView();
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
        var list = IsIconPicker ? IconItemsListBox : ItemsListBox;
        if (list.ItemCount == 0)
        {
            return;
        }

        var current = list.SelectedIndex;
        if (current < 0)
        {
            current = directionKey == Key.Up ? list.ItemCount : -1;
        }

        var next = directionKey == Key.Down
            ? Math.Min(list.ItemCount - 1, current + 1)
            : Math.Max(0, current - 1);

        list.SelectedIndex = next;
        var selectedItem = list.SelectedItem;
        if (selectedItem is not null)
        {
            list.ScrollIntoView(selectedItem);
        }
    }

    private void ToggleCurrentItemFromKeyboard()
    {
        if (IsIconPicker)
        {
            if (IconItemsListBox.SelectedItem is string key)
            {
                SelectIcon(key);
            }

            return;
        }

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
