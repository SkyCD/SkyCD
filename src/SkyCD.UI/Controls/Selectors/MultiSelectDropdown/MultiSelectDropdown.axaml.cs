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
    // Issue #1214: the tile grid cannot virtualize (a WrapPanel realizes every child), so the
    // emoji-style grid pages through the catalog instead of materializing thousands of icons.
    private const int IconTilePageSize = 120;
    private readonly ObservableCollection<string> iconTiles = [];
    private IReadOnlyList<string> iconTilePool = [];
    private IEnumerable<MultiSelectOptionItem>? trackedItems;
    private bool suppressInlineTextProcessing;
    private bool suppressOpenOnInlineFocus;
    private bool suppressIconSelection;
    private string searchText = string.Empty;
    private string iconSearchText = string.Empty;
    private string activeIconCategory = string.Empty;

    public MultiSelectDropdown()
    {
        InitializeComponent();
        this.Bind(PlaceholderTextProperty, new Binding($"[{ExtraKeys.Select}]")
        {
            Source = LocalizationService.Current,
            Priority = BindingPriority.Style
        });
        ItemsListBox.ItemsSource = filteredItems;
        IconTilesListBox.ItemsSource = iconTiles;
        IconTilesListBox.AddHandler(ScrollViewer.ScrollChangedEvent, OnIconTilesScrollChanged, RoutingStrategies.Bubble);
        IconTilesListBox.AddHandler(InputElement.PointerPressedEvent, OnIconTilesPointerPressed, RoutingStrategies.Tunnel);
        // Tunnel navigation so the grid owns its arrow keys before the ListBox moves the selection.
        IconTilesListBox.AddHandler(InputElement.KeyDownEvent, OnIconTilesKeyDown, RoutingStrategies.Tunnel);
        // Tunneling keeps Down available for moving from the search field into the grid; a plain
        // TextBox handler would lose it to caret movement.
        IconSearchTextBox.AddHandler(InputElement.KeyDownEvent, OnIconSearchKeyDown, RoutingStrategies.Tunnel);
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
            RebuildIconCategories();
            RefreshIconTiles();
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

        // The icon picker searches inside its popup; the collapsed field only mirrors the choice.
        if (IsIconPicker)
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
        if (suppressOpenOnInlineFocus)
        {
            return;
        }

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
        if (IsIconPicker)
        {
            if (e.Key is Key.Down or Key.Space or Key.Enter)
            {
                if (!DropdownPopup.IsOpen)
                {
                    DropdownPopup.IsOpen = true;
                }

                Dispatcher.UIThread.Post(() => FocusIconSearch(), DispatcherPriority.Input);
                e.Handled = true;
            }

            return;
        }

        if (e.Key != Key.Down && e.Key != Key.Up && e.Key != Key.Space && e.Key != Key.Enter)
        {
            return;
        }

        if (!DropdownPopup.IsOpen)
        {
            DropdownPopup.IsOpen = true;
            RefreshFilteredItems();
        }

        if (ItemsListBox.ItemCount == 0)
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            var targetIndex = e.Key == Key.Up
                ? Math.Max(0, ItemsListBox.ItemCount - 1)
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
        if (IsIconPicker)
        {
            RefreshIconTiles();
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

    // Issue #1214: the icon picker keeps its own search text and pack category, independent from
    // the inline multi-select filter, and renders a wrapping tile grid instead of a checkbox list.
    private void RefreshIconTiles()
    {
        if (!IsIconPicker || !DropdownPopup.IsOpen)
        {
            return;
        }

        var keys = IconKeys ?? [];
        iconTilePool = (string.IsNullOrWhiteSpace(iconSearchText)
            ? FilterIconKeysByCategory(keys)
            : keys.Where(key => key.Contains(iconSearchText, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        iconTiles.Clear();
        AppendIconTilePage();
        ApplyIconTileSelection();
    }

    private void AppendIconTilePage()
    {
        var start = iconTiles.Count;
        if (start >= iconTilePool.Count)
        {
            return;
        }

        var end = Math.Min(iconTilePool.Count, start + IconTilePageSize);
        for (var index = start; index < end; index++)
        {
            iconTiles.Add(iconTilePool[index]);
        }
    }

    private void EnsureIconTilesLoaded(int index)
    {
        while (index >= iconTiles.Count && iconTiles.Count < iconTilePool.Count)
        {
            AppendIconTilePage();
        }
    }

    private void OnIconTilesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // Avalonia 12 reports deltas only, so read the scroller's absolute values instead.
        if (!IsIconPicker ||
            !DropdownPopup.IsOpen ||
            iconTiles.Count >= iconTilePool.Count ||
            e.Source is not ScrollViewer scroller ||
            scroller.Viewport.Height <= 0 ||
            scroller.Extent.Height - (scroller.Offset.Y + scroller.Viewport.Height) > IconTilePageSize)
        {
            return;
        }

        AppendIconTilePage();
    }

    private void ApplyIconTileSelection()
    {
        suppressIconSelection = true;
        IconTilesListBox.SelectedItem = SelectedIconKey is { } selected
            ? iconTiles.FirstOrDefault(key => string.Equals(key, selected, StringComparison.OrdinalIgnoreCase))
            : null;
        suppressIconSelection = false;
    }

    private IEnumerable<string> FilterIconKeysByCategory(IReadOnlyList<string> keys)
    {
        if (string.IsNullOrWhiteSpace(activeIconCategory) || activeIconCategory == AllCategoryLabel)
        {
            return keys;
        }

        return keys.Where(key => string.Equals(GetIconPackName(key), activeIconCategory,
            StringComparison.OrdinalIgnoreCase));
    }

    private string AllCategoryLabel => LocalizationService.Current.Get(UiKeys.AllIcons);

    private static string GetIconPackName(string key)
    {
        var separator = key.IndexOf(':');
        return separator > 0 ? key[..separator] : key;
    }

    private void RebuildIconCategories()
    {
        if (!IsIconPicker)
        {
            return;
        }

        var packs = (IconKeys ?? [])
            .Select(GetIconPackName)
            .Where(static pack => !string.IsNullOrWhiteSpace(pack))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static pack => pack, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var categories = new[] { AllCategoryLabel }.Concat(packs).ToArray();

        var selected = categories.Contains(activeIconCategory, StringComparer.Ordinal)
            ? activeIconCategory
            : AllCategoryLabel;
        activeIconCategory = selected;

        suppressIconSelection = true;
        IconCategoryList.ItemsSource = categories;
        IconCategoryList.SelectedItem = selected;
        suppressIconSelection = false;
    }

    private void OnIconCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressIconSelection)
        {
            return;
        }

        activeIconCategory = IconCategoryList.SelectedItem is string category
            ? category
            : AllCategoryLabel;
        RefreshIconTiles();
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

    private void OnDropdownPopupOpened(object? sender, EventArgs e)
    {
        if (!IsIconPicker)
        {
            return;
        }

        iconSearchText = string.Empty;
        suppressInlineTextProcessing = true;
        IconSearchTextBox.Text = string.Empty;
        suppressInlineTextProcessing = false;
        RebuildIconCategories();
        RefreshIconTiles();
        Dispatcher.UIThread.Post(FocusIconSearch, DispatcherPriority.Input);
    }

    private void OnDropdownPopupClosed(object? sender, EventArgs e)
    {
        searchText = string.Empty;
        iconSearchText = string.Empty;
        activeIconCategory = AllCategoryLabel;
        iconTiles.Clear();
        iconTilePool = [];
        suppressInlineTextProcessing = true;
        IconSearchTextBox.Text = string.Empty;
        suppressInlineTextProcessing = false;
        RefreshSelectedItemsView();
    }

    private void FocusIconSearch()
    {
        if (DropdownPopup.IsOpen)
        {
            IconSearchTextBox.Focus();
        }
    }

    private void OnIconSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        iconSearchText = IconSearchTextBox.Text?.Trim() ?? string.Empty;
        RefreshIconTiles();
    }

    private void FocusSelectedIconTile()
    {
        if (!DropdownPopup.IsOpen)
        {
            return;
        }

        if (IconTilesListBox.SelectedIndex >= 0 &&
            IconTilesListBox.ContainerFromIndex(IconTilesListBox.SelectedIndex) is { } tile)
        {
            tile.Focus();
            return;
        }

        IconTilesListBox.Focus();
    }

    private void OnIconSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseIconPicker();
                e.Handled = true;
                break;
            case Key.Down when IconTilesListBox.ItemCount > 0:
                if (IconTilesListBox.SelectedIndex < 0)
                {
                    IconTilesListBox.SelectedIndex = 0;
                }

                Dispatcher.UIThread.Post(FocusSelectedIconTile, DispatcherPriority.Input);
                e.Handled = true;
                break;
            case Key.Enter when IconTilesListBox.SelectedItem is string key:
                SelectIcon(key);
                e.Handled = true;
                break;
        }
    }

    private void OnIconTilesKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseIconPicker();
                e.Handled = true;
                return;
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                MoveIconTileSelection(e.Key);
                e.Handled = true;
                return;
            case Key.Space:
            case Key.Enter:
                if (IconTilesListBox.SelectedItem is string key)
                {
                    SelectIcon(key);
                }

                e.Handled = true;
                return;
        }
    }

    private void MoveIconTileSelection(Key key)
    {
        var count = iconTilePool.Count;
        if (count == 0)
        {
            return;
        }

        var current = IconTilesListBox.SelectedIndex < 0 ? 0 : IconTilesListBox.SelectedIndex;
        var columns = GetIconTileColumnCount();
        var next = key switch
        {
            Key.Left => Math.Max(0, current - 1),
            Key.Right => Math.Min(count - 1, current + 1),
            Key.Up => Math.Max(0, current - columns),
            Key.Down => Math.Min(count - 1, current + columns),
            _ => current
        };

        EnsureIconTilesLoaded(next);
        IconTilesListBox.SelectedIndex = Math.Min(next, iconTiles.Count - 1);
        if (IconTilesListBox.SelectedItem is string selected)
        {
            IconTilesListBox.ScrollIntoView(selected);
        }
    }

    private int GetIconTileColumnCount()
    {
        if (IconTilesListBox.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault() is not { } panel ||
            panel.GetVisualDescendants().OfType<ListBoxItem>().FirstOrDefault() is not { } firstTile)
        {
            return 1;
        }

        var spacing = panel.ItemSpacing;
        var cellWidth = firstTile.Bounds.Width + spacing;
        return cellWidth > 0
            ? Math.Max(1, (int)((panel.Bounds.Width + spacing) / cellWidth))
            : 1;
    }

    private void OnIconPickerCloseClick(object? sender, RoutedEventArgs e)
    {
        CloseIconPicker();
    }

    private void CloseIconPicker()
    {
        DropdownPopup.IsOpen = false;
        iconSearchText = string.Empty;
        activeIconCategory = AllCategoryLabel;
        RefreshSelectedItemsView();
        FocusInlineField();
    }

    private void FocusInlineField()
    {
        suppressOpenOnInlineFocus = true;
        try
        {
            InlineSearchTextBox.Focus();
        }
        finally
        {
            suppressOpenOnInlineFocus = false;
        }
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

    private void OnIconTilesPointerPressed(object? sender, PointerPressedEventArgs e)
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
        CloseIconPicker();
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
        if (ItemsListBox.ItemCount == 0)
        {
            return;
        }

        var current = ItemsListBox.SelectedIndex;
        if (current < 0)
        {
            current = directionKey == Key.Up ? ItemsListBox.ItemCount : -1;
        }

        var next = directionKey == Key.Down
            ? Math.Min(ItemsListBox.ItemCount - 1, current + 1)
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
