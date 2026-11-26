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
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    public static readonly StyledProperty<IDataTemplate?> ItemTileTemplateProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, IDataTemplate?>(nameof(ItemTileTemplate));

    public static readonly StyledProperty<bool> ShowItemLabelsProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(ShowItemLabels), defaultValue: true);

    public static readonly StyledProperty<bool> IsIconPickerProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, bool>(nameof(IsIconPicker));

    public static readonly StyledProperty<IReadOnlyList<string>?> IconKeysProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, IReadOnlyList<string>?>(nameof(IconKeys));

    public static readonly StyledProperty<string?> SelectedIconKeyProperty =
        AvaloniaProperty.Register<MultiSelectDropdown, string?>(nameof(SelectedIconKey), defaultBindingMode: BindingMode.TwoWay);

    private static readonly string[] PreferredCategoryGlyphs =
    [
        "Star", "Heart", "Home", "House", "Smile", "Sun", "Sparkles", "Check", "Book", "Circle",
        "LayoutGrid"
    ];

    private const int FallbackIconColumnCount = 10;
    private readonly ObservableCollection<MultiSelectOptionItem> filteredItems = [];
    private readonly ObservableCollection<IconCategoryItem> iconCategories = [];
    private readonly ObservableCollection<IconTileRow> iconRows = [];
    private IReadOnlyList<string>? iconCatalogSource;
    private string[] iconCatalog = [];
    private IconTileItem[] allIconTiles = [];
    private IconTileItem[] visibleIconTiles = [];
    private int iconColumnCount = FallbackIconColumnCount;
    private IconTileItem? highlightedTile;
    private IEnumerable<MultiSelectOptionItem>? trackedItems;
    private bool suppressInlineTextProcessing;
    private bool suppressOpenOnInlineFocus;
    private bool suppressIconCategoryEvents;
    private bool suppressCategorySync;
    private string searchText = string.Empty;
    private string iconSearchText = string.Empty;

    public MultiSelectDropdown()
    {
        InitializeComponent();
        this.Bind(PlaceholderTextProperty, new Binding($"[{ExtraKeys.Select}]")
        {
            Source = LocalizationService.Current,
            Priority = BindingPriority.Style
        });
        ItemsListBox.ItemsSource = filteredItems;
        IconCategoryList.ItemsSource = iconCategories;
        IconTilesListBox.ItemsSource = iconRows;
        IconTilesListBox.AddHandler(ScrollViewer.ScrollChangedEvent, OnIconTilesScrollChanged, RoutingStrategies.Bubble);
        // Tunnel navigation so the sheet owns its arrow keys before the ListBox moves row selection.
        IconTilesListBox.AddHandler(InputElement.KeyDownEvent, OnIconTilesKeyDown, RoutingStrategies.Tunnel);
        IconTilesListBox.SizeChanged += OnIconTilesSizeChanged;
        // Tunneling keeps Down available for moving from the search field into the sheet; a plain
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

    public IDataTemplate? ItemTileTemplate
    {
        get => GetValue(ItemTileTemplateProperty);
        set => SetValue(ItemTileTemplateProperty, value);
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
            if (e.Key == Key.Escape)
            {
                CloseIconPicker();
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Down or Key.Up or Key.Space or Key.Enter)
            {
                if (!DropdownPopup.IsOpen)
                {
                    DropdownPopup.IsOpen = true;
                }

                Dispatcher.UIThread.Post(FocusIconSearch, DispatcherPriority.Input);
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

    // Issue #1214: the sheet holds every pack in one scrolling list. Rows of tiles keep the sheet
    // virtualized (a WrapPanel would realize thousands of icons and freeze the popup), the search
    // field filters the same sheet, and the pack strip scrolls it rather than filtering it.
    private void RefreshIconTiles()
    {
        if (!IsIconPicker || !DropdownPopup.IsOpen)
        {
            return;
        }

        EnsureIconCatalog();
        var query = iconSearchText;
        visibleIconTiles = query.Length == 0
            ? allIconTiles
            : allIconTiles.Where(tile => tile.Key.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        RebuildIconRows();
        SetHighlightedTile(FindVisibleTile(SelectedIconKey));
        ScrollIconSheetToStart();
    }

    private void EnsureIconCatalog()
    {
        var keys = IconKeys ?? [];
        if (ReferenceEquals(iconCatalogSource, keys))
        {
            return;
        }

        iconCatalogSource = keys;
        iconCatalog = keys
            .OrderBy(GetIconPackName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        allIconTiles = iconCatalog.Select(static key => new IconTileItem(key)).ToArray();
        RebuildIconCategories();
    }

    private void RebuildIconRows()
    {
        iconRows.Clear();
        for (var start = 0; start < visibleIconTiles.Length; start += iconColumnCount)
        {
            iconRows.Add(new IconTileRow(visibleIconTiles, start, Math.Min(iconColumnCount, visibleIconTiles.Length - start)));
        }
    }

    private IconTileItem? FindVisibleTile(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return visibleIconTiles.FirstOrDefault(tile => string.Equals(tile.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private void SetHighlightedTile(IconTileItem? tile)
    {
        if (ReferenceEquals(highlightedTile, tile))
        {
            return;
        }

        if (highlightedTile is not null)
        {
            highlightedTile.IsHighlighted = false;
        }

        highlightedTile = tile;
        if (tile is not null)
        {
            tile.IsHighlighted = true;
        }
    }

    private void ScrollIconSheetToStart()
    {
        if (iconRows.Count > 0)
        {
            IconTilesListBox.ScrollIntoView(iconRows[0]);
        }
    }

    private void OnIconTilesSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!IsIconPicker || !DropdownPopup.IsOpen)
        {
            return;
        }

        var columns = CalculateIconColumns();
        if (columns == iconColumnCount)
        {
            return;
        }

        iconColumnCount = columns;
        RebuildIconRows();
    }

    private int CalculateIconColumns()
    {
        var width = IconTilesListBox.Bounds.Width;
        if (width <= 0)
        {
            return iconColumnCount;
        }

        var tileSize = GetResourceDouble("SkyIconTileSize", 32);
        var spacing = GetResourceDouble("SkySpaceXxs", 2);
        var step = tileSize + spacing;
        return Math.Max(1, (int)((width + spacing) / step));
    }

    private double GetResourceDouble(string key, double fallback) =>
        this.TryFindResource(key, out var value) && value is double number ? number : fallback;

    private void OnIconTilesScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (!IsIconPicker || !DropdownPopup.IsOpen)
        {
            return;
        }

        SyncCategoryWithScroll();
    }

    private void SyncCategoryWithScroll()
    {
        if (suppressCategorySync || iconRows.Count == 0 || visibleIconTiles.Length == 0)
        {
            return;
        }

        var firstRow = GetTopVisibleRow();
        if (firstRow is null)
        {
            return;
        }

        var index = Math.Min(firstRow.StartIndex, visibleIconTiles.Length - 1);
        var pack = GetIconPackName(visibleIconTiles[index].Key);
        var category = iconCategories.FirstOrDefault(candidate =>
            string.Equals(candidate.Label, pack, StringComparison.OrdinalIgnoreCase));
        if (category is null || ReferenceEquals(IconCategoryList.SelectedItem, category))
        {
            return;
        }

        SelectCategoryItem(category);
    }

    // Virtualizing panels keep rows realized just above the viewport, so the cached container with
    // the lowest index can belong to the previous pack; only rows still touching the viewport count.
    private IconTileRow? GetTopVisibleRow()
    {
        var viewer = GetSheetScrollViewer();
        if (viewer is null)
        {
            return null;
        }

        IconTileRow? topRow = null;
        var topStart = int.MaxValue;
        foreach (var container in IconTilesListBox.GetVisualDescendants().OfType<ListBoxItem>())
        {
            if (container.DataContext is not IconTileRow row)
            {
                continue;
            }

            var position = container.TranslatePoint(default, viewer);
            if (position is null || position.Value.Y + container.Bounds.Height <= 0.5)
            {
                continue;
            }

            if (row.StartIndex < topStart)
            {
                topStart = row.StartIndex;
                topRow = row;
            }
        }

        return topRow;
    }

    private ScrollViewer? GetSheetScrollViewer() =>
        IconTilesListBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void SelectCategoryItem(IconCategoryItem? category)
    {
        suppressIconCategoryEvents = true;
        try
        {
            IconCategoryList.SelectedItem = category;
        }
        finally
        {
            suppressIconCategoryEvents = false;
        }

        if (category is not null)
        {
            IconCategoryList.ScrollIntoView(category);
        }
    }

    private void RebuildIconCategories()
    {
        iconCategories.Clear();

        string? packName = null;
        var startIndex = 0;
        string? representative = null;
        var representativeRank = int.MaxValue;

        for (var index = 0; index < iconCatalog.Length; index++)
        {
            var key = iconCatalog[index];
            var pack = GetIconPackName(key);
            if (!string.Equals(pack, packName, StringComparison.OrdinalIgnoreCase))
            {
                if (packName is not null && representative is not null)
                {
                    iconCategories.Add(new IconCategoryItem(packName, representative, startIndex));
                }

                packName = pack;
                startIndex = index;
                representative = key;
                representativeRank = GetCategoryGlyphRank(key);
                continue;
            }

            var rank = GetCategoryGlyphRank(key);
            if (rank < representativeRank)
            {
                representative = key;
                representativeRank = rank;
            }
        }

        if (packName is not null && representative is not null)
        {
            iconCategories.Add(new IconCategoryItem(packName, representative, startIndex));
        }
    }

    private static int GetCategoryGlyphRank(string key)
    {
        var value = GetIconValueName(key);
        for (var index = 0; index < PreferredCategoryGlyphs.Length; index++)
        {
            if (string.Equals(value, PreferredCategoryGlyphs[index], StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return int.MaxValue;
    }

    private void OnIconCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (suppressIconCategoryEvents || IconCategoryList.SelectedItem is not IconCategoryItem category)
        {
            return;
        }

        JumpToIconCategory(category);
    }

    private void JumpToIconCategory(IconCategoryItem category)
    {
        if (iconSearchText.Length > 0)
        {
            IconSearchTextBox.Text = string.Empty;
        }

        if (visibleIconTiles.Length == 0)
        {
            return;
        }

        var index = Math.Clamp(category.StartIndex, 0, visibleIconTiles.Length - 1);
        var row = index / iconColumnCount;
        if (row < iconRows.Count)
        {
            ScrollIconRowToTop(iconRows[row]);
        }
    }

    // ScrollIntoView only guarantees the row is somewhere in the viewport, often at its bottom edge,
    // which would leave the previous pack on top and hand the highlight back to it. After the scroll
    // settles the row is nudged to the top edge; sync stays off until then so the jump is not undone.
    private void ScrollIconRowToTop(IconTileRow row)
    {
        suppressCategorySync = true;
        IconTilesListBox.ScrollIntoView(row);
        Dispatcher.UIThread.Post(() => AlignIconRowToTop(row), DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => suppressCategorySync = false, DispatcherPriority.Background);
    }

    private void AlignIconRowToTop(IconTileRow row)
    {
        if (!DropdownPopup.IsOpen)
        {
            return;
        }

        var viewer = GetSheetScrollViewer();
        var container = IconTilesListBox.GetVisualDescendants().OfType<ListBoxItem>()
            .FirstOrDefault(item => ReferenceEquals(item.DataContext, row));
        if (viewer is null || container?.TranslatePoint(default, viewer) is not { } position)
        {
            return;
        }

        var offset = Math.Max(0, viewer.Offset.Y + position.Y);
        if (Math.Abs(offset - viewer.Offset.Y) > 0.5)
        {
            viewer.Offset = new Vector(viewer.Offset.X, offset);
        }
    }

    private static string GetIconPackName(string key)
    {
        var separator = key.IndexOf(':');
        return separator > 0 ? key[..separator] : key;
    }

    private static string GetIconValueName(string key)
    {
        var separator = key.IndexOf(':');
        return separator > 0 ? key[(separator + 1)..] : key;
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
        RefreshIconTiles();
        SelectCategoryItem(iconCategories.FirstOrDefault());
        Dispatcher.UIThread.Post(FocusIconSearch, DispatcherPriority.Input);
    }

    private void OnDropdownPopupClosed(object? sender, EventArgs e)
    {
        searchText = string.Empty;
        iconSearchText = string.Empty;
        suppressInlineTextProcessing = true;
        IconSearchTextBox.Text = string.Empty;
        suppressInlineTextProcessing = false;
        SetHighlightedTile(null);
        iconRows.Clear();
        visibleIconTiles = [];
        allIconTiles = [];
        iconCatalog = [];
        iconCatalogSource = null;
        iconCategories.Clear();
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
        if (suppressInlineTextProcessing)
        {
            return;
        }

        iconSearchText = IconSearchTextBox.Text?.Trim() ?? string.Empty;
        RefreshIconTiles();
    }

    private void FocusIconTiles()
    {
        if (DropdownPopup.IsOpen)
        {
            IconTilesListBox.Focus();
        }
    }

    private void OnIconSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                CloseIconPicker();
                e.Handled = true;
                break;
            case Key.Down when visibleIconTiles.Length > 0:
                EnterIconSheetFromSearch();
                e.Handled = true;
                break;
            case Key.Enter when highlightedTile is { } tile:
                SelectIcon(tile.Key);
                e.Handled = true;
                break;
        }
    }

    private void EnterIconSheetFromSearch()
    {
        var firstRow = GetTopVisibleRow();
        var startIndex = Math.Clamp(firstRow?.StartIndex ?? 0, 0, visibleIconTiles.Length - 1);
        SetHighlightedTile(visibleIconTiles[startIndex]);
        FocusIconTiles();
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
                MoveIconTileHighlight(e.Key);
                e.Handled = true;
                return;
            case Key.Space:
            case Key.Enter:
                if (highlightedTile is { } tile)
                {
                    SelectIcon(tile.Key);
                }

                e.Handled = true;
                return;
        }
    }

    private void MoveIconTileHighlight(Key key)
    {
        if (visibleIconTiles.Length == 0)
        {
            return;
        }

        var current = highlightedTile is null ? -1 : Array.IndexOf(visibleIconTiles, highlightedTile);
        if (current < 0)
        {
            current = 0;
        }

        var next = key switch
        {
            Key.Left => Math.Max(0, current - 1),
            Key.Right => Math.Min(visibleIconTiles.Length - 1, current + 1),
            Key.Up => Math.Max(0, current - iconColumnCount),
            Key.Down => Math.Min(visibleIconTiles.Length - 1, current + iconColumnCount),
            _ => current
        };

        SetHighlightedTile(visibleIconTiles[next]);
        var row = next / iconColumnCount;
        if (row < iconRows.Count)
        {
            IconTilesListBox.ScrollIntoView(iconRows[row]);
        }
    }

    private void CloseIconPicker()
    {
        DropdownPopup.IsOpen = false;
        iconSearchText = string.Empty;
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

    private void OnIconTilePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: IconTileItem tile })
        {
            SelectIcon(tile.Key);
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
