using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using SkyCD.UI.Controls.Preview;

namespace SkyCD.UI.Controls.Lists;

public partial class BrowserItemsView : UserControl
{
    private static readonly FuncValueConverter<object?, bool> HasNonEmptyValueConverter =
        new(value => !string.IsNullOrWhiteSpace(value?.ToString()));
    private static readonly FuncValueConverter<object?, IBrush> BrushFromStringConverter =
        new(value => Color.TryParse(value?.ToString(), out var color) ? new SolidColorBrush(color) : Brushes.White);

    private readonly DetailsListView? detailsListView;
    private readonly ListBox? listModeListBox;
    private readonly ListBox? iconGridListBox;
    private bool isUpdatingSelection;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<BrowserItemsView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<BrowserItemsView, object?>(nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<BrowserItemsView, IList?>(nameof(SelectedItems),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<BrowserViewMode> ViewModeProperty =
        AvaloniaProperty.Register<BrowserItemsView, BrowserViewMode>(nameof(ViewMode), BrowserViewMode.Details);

    public static readonly StyledProperty<ContextMenu?> ListContextMenuProperty =
        AvaloniaProperty.Register<BrowserItemsView, ContextMenu?>(nameof(ListContextMenu));

    /// <summary>Menu shown when the details header is right-clicked.</summary>
    public static readonly StyledProperty<ContextMenu?> HeaderContextMenuProperty =
        AvaloniaProperty.Register<BrowserItemsView, ContextMenu?>(nameof(HeaderContextMenu));

    public static readonly StyledProperty<double> ListMinWidthProperty =
        AvaloniaProperty.Register<BrowserItemsView, double>(nameof(ListMinWidth), 240);

    public static readonly StyledProperty<double> BrowserGridItemWidthProperty =
        AvaloniaProperty.Register<BrowserItemsView, double>(nameof(BrowserGridItemWidth), 220);

    public static readonly StyledProperty<double> BrowserGridItemHeightProperty =
        AvaloniaProperty.Register<BrowserItemsView, double>(nameof(BrowserGridItemHeight), 60);

    public static readonly StyledProperty<bool> IsTilesModeProperty =
        AvaloniaProperty.Register<BrowserItemsView, bool>(nameof(IsTilesMode));

    public static readonly StyledProperty<IEnumerable<BrowserDetailsColumn>?> DetailsColumnsProperty =
        AvaloniaProperty.Register<BrowserItemsView, IEnumerable<BrowserDetailsColumn>?>(nameof(DetailsColumns));

    public static readonly StyledProperty<IValueConverter?> IconConverterProperty =
        AvaloniaProperty.Register<BrowserItemsView, IValueConverter?>(nameof(IconConverter));

    public static readonly StyledProperty<IValueConverter?> StatusIconConverterProperty =
        AvaloniaProperty.Register<BrowserItemsView, IValueConverter?>(nameof(StatusIconConverter));

    /// <summary>
    /// Optional source of cached thumbnails. When set, realized items overlay an asynchronously loaded
    /// thumbnail on top of their type icon; when null the list renders icons only, exactly as before.
    /// </summary>
    public static readonly StyledProperty<IPreviewThumbnailProvider?> ThumbnailProviderProperty =
        AvaloniaProperty.Register<BrowserItemsView, IPreviewThumbnailProvider?>(nameof(ThumbnailProvider));

    public IPreviewThumbnailProvider? ThumbnailProvider
    {
        get => GetValue(ThumbnailProviderProperty);
        set => SetValue(ThumbnailProviderProperty, value);
    }

    public IValueConverter? StatusIconConverter
    {
        get => GetValue(StatusIconConverterProperty);
        set => SetValue(StatusIconConverterProperty, value);
    }

    public new event EventHandler<TappedEventArgs>? DoubleTapped;
    public new event EventHandler<ContextRequestedEventArgs>? ContextRequested;

    /// <summary>Raised when a details header is dragged onto a new position.</summary>
    public event EventHandler<DetailsColumnReorderedEventArgs>? DetailsColumnReordered;

    /// <summary>Raised when a details header splitter drag finishes with a new width.</summary>
    public event EventHandler<DetailsColumnResizedEventArgs>? DetailsColumnResized;

    public BrowserItemsView()
    {
        AvaloniaXamlLoader.Load(this);

        detailsListView = this.FindControl<DetailsListView>("DetailsListView");
        listModeListBox = this.FindControl<ListBox>("ListModeListBox");
        iconGridListBox = this.FindControl<ListBox>("IconGridListBox");
        if (detailsListView is null || listModeListBox is null || iconGridListBox is null)
        {
            return;
        }

        detailsListView.DoubleTapped += (_, e) => DoubleTapped?.Invoke(this, e);
        detailsListView.ContextRequested += (_, e) => ContextRequested?.Invoke(this, e);
        detailsListView.PropertyChanged += (_, e) =>
        {
            if (isUpdatingSelection)
            {
                return;
            }

            if (ViewMode == BrowserViewMode.Details)
            {
                isUpdatingSelection = true;
                try
                {
                    if (e.Property == DetailsListView.SelectedItemProperty)
                    {
                        SelectedItem = detailsListView.SelectedItem;
                    }
                    else if (e.Property == DetailsListView.SelectedItemsProperty)
                    {
                        SelectedItems = detailsListView.SelectedItems;
                    }
                }
                finally
                {
                    isUpdatingSelection = false;
                }
            }
        };

        listModeListBox.DoubleTapped += (_, e) => DoubleTapped?.Invoke(this, e);
        listModeListBox.ContextRequested += (_, e) => ContextRequested?.Invoke(this, e);
        listModeListBox.SelectionChanged += (_, _) =>
        {
            if (isUpdatingSelection)
            {
                return;
            }

            if (ViewMode == BrowserViewMode.List)
            {
                isUpdatingSelection = true;
                try
                {
                    SelectedItem = listModeListBox.SelectedItem;
                    SelectedItems = listModeListBox.SelectedItems?.Cast<object>().ToList();
                }
                finally
                {
                    isUpdatingSelection = false;
                }
            }
        };

        iconGridListBox.DoubleTapped += (_, e) => DoubleTapped?.Invoke(this, e);
        iconGridListBox.ContextRequested += (_, e) => ContextRequested?.Invoke(this, e);
        iconGridListBox.SelectionChanged += (_, _) =>
        {
            if (isUpdatingSelection)
            {
                return;
            }

            if (ViewMode is BrowserViewMode.Tiles or BrowserViewMode.SmallIcons or BrowserViewMode.LargeIcons)
            {
                isUpdatingSelection = true;
                try
                {
                    SelectedItem = iconGridListBox.SelectedItem;
                    SelectedItems = iconGridListBox.SelectedItems?.Cast<object>().ToList();
                }
                finally
                {
                    isUpdatingSelection = false;
                }
            }
        };

        UpdateViewMode();
        UpdateListTemplate();
        UpdateIconGridTemplate();
        UpdateDetailsTemplate();
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public IList? SelectedItems
    {
        get => GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    public BrowserViewMode ViewMode
    {
        get => GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    public IEnumerable<BrowserDetailsColumn>? DetailsColumns
    {
        get => GetValue(DetailsColumnsProperty);
        set => SetValue(DetailsColumnsProperty, value);
    }

    public ContextMenu? ListContextMenu
    {
        get => GetValue(ListContextMenuProperty);
        set => SetValue(ListContextMenuProperty, value);
    }

    public ContextMenu? HeaderContextMenu
    {
        get => GetValue(HeaderContextMenuProperty);
        set => SetValue(HeaderContextMenuProperty, value);
    }

    public double ListMinWidth
    {
        get => GetValue(ListMinWidthProperty);
        set => SetValue(ListMinWidthProperty, value);
    }

    public double BrowserGridItemWidth
    {
        get => GetValue(BrowserGridItemWidthProperty);
        set => SetValue(BrowserGridItemWidthProperty, value);
    }

    public double BrowserGridItemHeight
    {
        get => GetValue(BrowserGridItemHeightProperty);
        set => SetValue(BrowserGridItemHeightProperty, value);
    }

    public bool IsTilesMode
    {
        get => GetValue(IsTilesModeProperty);
        private set => SetValue(IsTilesModeProperty, value);
    }

    public IValueConverter? IconConverter
    {
        get => GetValue(IconConverterProperty);
        set => SetValue(IconConverterProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ViewModeProperty)
        {
            UpdateViewMode();
        }
        else if (change.Property == SelectedItemProperty)
        {
            if (!isUpdatingSelection)
            {
                var active = GetActiveControl();
                if (active is not null)
                {
                    isUpdatingSelection = true;
                    try
                    {
                        SyncSelectionToControl(active, SelectedItem, SelectedItems);
                        RevealSelectedItem(active, SelectedItem);
                    }
                    finally
                    {
                        isUpdatingSelection = false;
                    }
                }
            }
        }
        else if (change.Property == SelectedItemsProperty)
        {
            if (!isUpdatingSelection)
            {
                var active = GetActiveControl();
                if (active is not null)
                {
                    isUpdatingSelection = true;
                    try
                    {
                        SyncSelectionToControl(active, SelectedItem, SelectedItems);
                    }
                    finally
                    {
                        isUpdatingSelection = false;
                    }
                }
            }
        }
        else if (change.Property == IconConverterProperty || change.Property == StatusIconConverterProperty || change.Property == DetailsColumnsProperty ||
                 change.Property == HeaderContextMenuProperty ||
                 change.Property == ThumbnailProviderProperty)
        {
            UpdateListTemplate();
            UpdateIconGridTemplate();
            UpdateDetailsTemplate();
        }
        else if (change.Property == BrowserGridItemWidthProperty || change.Property == BrowserGridItemHeightProperty)
        {
            UpdateListTemplate();
            UpdateIconGridTemplate();
        }
    }

    public void FocusActiveControl()
    {
        var active = GetActiveControl();
        if (active is null)
        {
            return;
        }

        active.Focus();

        // Focus the specific selected item so keyboard navigation works immediately
        if (active is ListBox listBox && listBox.SelectedItem is not null)
        {
            var container = listBox.ContainerFromItem(listBox.SelectedItem) as ListBoxItem;
            container?.Focus();
        }
        else if (active is DetailsListView details && details.SelectedItem is not null)
        {
            var inner = details.FindControl<ListBox>("InnerListBox");
            var container = inner?.ContainerFromItem(details.SelectedItem) as ListBoxItem;
            container?.Focus();
        }
    }

    private Control? GetActiveControl()
    {
        return ViewMode switch
        {
            BrowserViewMode.Details => detailsListView,
            BrowserViewMode.List => listModeListBox,
            _ => iconGridListBox
        };
    }

    private void SyncSelectionToControl(Control? control, object? selectedItem, IList? selectedItems)
    {
        if (control is null)
        {
            return;
        }

        if (control is DetailsListView details)
        {
            var inner = details.FindControl<ListBox>("InnerListBox");
            if (inner is not null)
            {
                ApplySelectionToListBox(inner, selectedItem, selectedItems);
            }

            details.SelectedItem = selectedItem;
            details.SelectedItems = selectedItems;
        }
        else if (control is ListBox listBox)
        {
            ApplySelectionToListBox(listBox, selectedItem, selectedItems);
        }
    }

    private static void ApplySelectionToListBox(ListBox listBox, object? selectedItem, IList? selectedItems)
    {
        if (selectedItems is { Count: > 0 } && listBox.Selection is not null)
        {
            var currentSelection = listBox.SelectedItems?.Cast<object>().ToHashSet();
            var newSelection = selectedItems.Cast<object>().Where(static x => x != null).ToHashSet();
            if (currentSelection != null && currentSelection.SetEquals(newSelection))
            {
                return;
            }

            listBox.Selection.Clear();
            var items = listBox.ItemsSource?.Cast<object>().ToList();
            if (items is not null)
            {
                foreach (var sel in selectedItems)
                {
                    if (sel is null)
                    {
                        continue;
                    }

                    var idx = items.IndexOf(sel);
                    if (idx >= 0)
                    {
                        listBox.Selection.Select(idx);
                    }
                }
            }
        }
        else if (selectedItems is { Count: 0 })
        {
            listBox.Selection?.Clear();
            listBox.SelectedItem = null;
        }
        else
        {
            if (!Equals(listBox.SelectedItem, selectedItem))
            {
                listBox.SelectedItem = selectedItem;
            }
        }
    }

    private static void RevealSelectedItem(Control? control, object? item)
    {
        if (item is null || control is null)
        {
            return;
        }

        if (control is DetailsListView details)
        {
            details.ScrollIntoView(item);
        }
        else if (control is ListBox listBox)
        {
            listBox.ScrollIntoView(item);
        }
    }

    private void UpdateViewMode()
    {
        if (detailsListView is null || listModeListBox is null || iconGridListBox is null)
        {
            return;
        }

        var currentItem = SelectedItem;
        var currentItems = SelectedItems;

        detailsListView.IsVisible = ViewMode == BrowserViewMode.Details;
        listModeListBox.IsVisible = ViewMode == BrowserViewMode.List;
        iconGridListBox.IsVisible =
            ViewMode is BrowserViewMode.Tiles or BrowserViewMode.SmallIcons or BrowserViewMode.LargeIcons;
        IsTilesMode = ViewMode == BrowserViewMode.Tiles;

        UpdateIconGridTemplate();

        var activeControl = GetActiveControl();
        if (activeControl is not null)
        {
            isUpdatingSelection = true;
            try
            {
                SyncSelectionToControl(activeControl, currentItem, currentItems);
            }
            finally
            {
                isUpdatingSelection = false;
            }

            SelectedItem = currentItem;
            SelectedItems = currentItems;

            RevealSelectedItem(activeControl, currentItem);
        }
    }

    private void UpdateDetailsTemplate()
    {
        if (detailsListView is null)
        {
            return;
        }

        var detailsColumns = (DetailsColumns ?? []).ToList();
        detailsListView.HeaderContent = BuildDetailsHeaderContent(detailsColumns);
        detailsListView.RowTemplate = BuildDetailsRowTemplate(detailsColumns);
    }

    private void UpdateListTemplate()
    {
        if (listModeListBox is null)
        {
            return;
        }

        listModeListBox.ItemTemplate = new FuncDataTemplate<object?>((item, _) =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(4, 2),
                Height = 22,
                MinWidth = 120,
                MaxWidth = 240,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(BuildIconWithStatusIndicator(16, 16));

            var name = new TextBlock
            {
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.Bind(TextBlock.TextProperty, new Binding("DisplayName"));
            panel.Children.Add(name);

            return panel;
        });
    }

    private void UpdateIconGridTemplate()
    {
        if (iconGridListBox is null)
        {
            return;
        }

        iconGridListBox.ItemTemplate = ViewMode switch
        {
            BrowserViewMode.SmallIcons => BuildSmallIconsTemplate(),
            BrowserViewMode.LargeIcons => BuildLargeIconsTemplate(),
            BrowserViewMode.Tiles => BuildTilesTemplate(),
            _ => BuildTilesTemplate()
        };
    }

    private IDataTemplate BuildSmallIconsTemplate()
    {
        return new FuncDataTemplate<object?>((item, _) =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(4, 2),
                Height = 22,
                MinWidth = 100,
                MaxWidth = BrowserGridItemWidth > 0 ? BrowserGridItemWidth : 160,
                VerticalAlignment = VerticalAlignment.Center
            };

            panel.Children.Add(BuildIconWithStatusIndicator(16, 16));

            var name = new TextBlock
            {
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.Bind(TextBlock.TextProperty, new Binding("DisplayName"));
            panel.Children.Add(name);

            return panel;
        });
    }

    private IDataTemplate BuildLargeIconsTemplate()
    {
        return new FuncDataTemplate<object?>((item, _) =>
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(4),
                Width = BrowserGridItemWidth > 0 ? Math.Min(BrowserGridItemWidth, 120) : 90,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            panel.Children.Add(BuildIconWithStatusIndicator(32, 32, HorizontalAlignment.Center));

            var name = new TextBlock
            {
                FontSize = 12,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            name.Bind(TextBlock.TextProperty, new Binding("DisplayName"));
            panel.Children.Add(name);

            return panel;
        });
    }

    private IDataTemplate BuildTilesTemplate()
    {
        return new FuncDataTemplate<object?>((item, _) =>
        {
            var grid = new Grid
            {
                Margin = new Thickness(4),
                Width = BrowserGridItemWidth > 0 ? BrowserGridItemWidth : 260,
                Height = BrowserGridItemHeight > 0 ? BrowserGridItemHeight : 60,
                ColumnDefinitions = new ColumnDefinitions("Auto,*")
            };

            var iconContainer = BuildIconWithStatusIndicator(32, 32, HorizontalAlignment.Left);
            iconContainer.Margin = new Thickness(0, 0, 8, 0);
            iconContainer.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(iconContainer, 0);
            grid.Children.Add(iconContainer);

            var details = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };

            var name = new TextBlock
            {
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            name.Bind(TextBlock.TextProperty, new Binding("DisplayName"));
            details.Children.Add(name);

            var type = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            type.Bind(TextBlock.TextProperty, SkyCD.UI.Localization.DisplayTypeBinding.Create());
            details.Children.Add(type);

            var size = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.75,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            size.Bind(TextBlock.TextProperty, new Binding("DisplaySize"));
            details.Children.Add(size);

            Grid.SetColumn(details, 1);
            grid.Children.Add(details);

            return grid;
        });
    }

    private object BuildDetailsHeaderContent(IReadOnlyList<BrowserDetailsColumn> detailsColumns)
    {
        var grid = new Grid
        {
            ColumnDefinitions = BuildDetailsColumnDefinitions(detailsColumns, isHeader: true)
        };

        if (HeaderContextMenu is not null)
        {
            grid.ContextMenu = HeaderContextMenu;
        }

        var headerCells = new List<Grid>(detailsColumns.Count);
        var pointerDrag = new DetailsHeaderPointerDrag();
        var headerCursor = new Cursor(StandardCursorType.SizeAll);

        for (var i = 0; i < detailsColumns.Count; i++)
        {
            var column = detailsColumns[i];
            var cellGrid = new Grid
            {
                ClipToBounds = true,
                Background = Brushes.Transparent
            };

            var cellBorder = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 3),
                MinHeight = 24,
                Cursor = column.IsRequired ? null : headerCursor
            };
            cellBorder.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ThemeControlBackgroundBrush"));
            cellBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeBorderLowBrush"));

            var headerText = new TextBlock
            {
                Text = column.Header,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                HorizontalAlignment = column.HeaderAlignment,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            cellBorder.Child = headerText;
            cellGrid.Children.Add(cellBorder);

            var splitter = new Border
            {
                Width = 8,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.SizeWestEast)
            };

            var targetColumn = column;
            var isDragging = false;
            var dragStartX = 0.0;
            var startWidth = 0.0;
            var widthChanged = false;

            splitter.PointerPressed += (s, e) =>
            {
                if (e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed)
                {
                    isDragging = true;
                    widthChanged = false;
                    dragStartX = e.GetPosition(grid).X;
                    startWidth = targetColumn.Width.GridUnitType == GridUnitType.Pixel
                        ? targetColumn.Width.Value
                        : cellGrid.Bounds.Width;
                    e.Pointer.Capture(splitter);
                    e.Handled = true;
                }
            };

            splitter.PointerMoved += (s, e) =>
            {
                if (isDragging)
                {
                    var currentX = e.GetPosition(grid).X;
                    var delta = currentX - dragStartX;
                    var newWidth = Math.Max(targetColumn.MinWidth, startWidth + delta);
                    if (Math.Abs(newWidth - targetColumn.Width.Value) > 0.5 ||
                        targetColumn.Width.GridUnitType != GridUnitType.Pixel)
                    {
                        widthChanged = true;
                    }

                    targetColumn.Width = new GridLength(newWidth, GridUnitType.Pixel);
                    e.Handled = true;
                }
            };

            splitter.PointerReleased += (s, e) =>
            {
                if (isDragging)
                {
                    isDragging = false;
                    e.Pointer.Capture(null);
                    RaiseDetailsColumnResized(targetColumn, widthChanged);
                    widthChanged = false;
                    e.Handled = true;
                }
            };

            splitter.PointerCaptureLost += (_, _) =>
            {
                if (isDragging)
                {
                    isDragging = false;
                    RaiseDetailsColumnResized(targetColumn, widthChanged);
                    widthChanged = false;
                }
            };

            cellGrid.Children.Add(splitter);

            if (!column.IsRequired)
            {
                var columnIndex = i;
                cellGrid.PointerPressed += (_, e) =>
                {
                    var point = e.GetCurrentPoint(cellGrid);
                    if (!point.Properties.IsLeftButtonPressed)
                    {
                        return;
                    }

                    pointerDrag.Begin(e.Pointer, point.Position.X, columnIndex, targetColumn.Id);
                    e.Pointer.Capture(grid);
                    e.Handled = true;
                };
            }

            Grid.SetColumn(cellGrid, i);
            grid.Children.Add(cellGrid);
            headerCells.Add(cellGrid);
        }

        var leadingRequiredCount = 0;
        while (leadingRequiredCount < detailsColumns.Count && detailsColumns[leadingRequiredCount].IsRequired)
        {
            leadingRequiredCount++;
        }

        grid.PointerMoved += (_, e) =>
        {
            if (!pointerDrag.IsActive || pointerDrag.Pointer != e.Pointer)
            {
                return;
            }

            if (!pointerDrag.IsDragging &&
                Math.Abs(e.GetPosition(grid).X - pointerDrag.StartX) > DetailsHeaderDragThreshold)
            {
                pointerDrag.IsDragging = true;
                grid.Cursor = headerCursor;
            }

            e.Handled = pointerDrag.IsDragging;
        };

        grid.PointerReleased += (_, e) =>
        {
            if (!pointerDrag.IsActive || pointerDrag.Pointer != e.Pointer)
            {
                return;
            }

            grid.ClearValue(CursorProperty);

            var wasDragging = pointerDrag.IsDragging;
            var sourceIndex = pointerDrag.SourceVisibleIndex;
            var columnId = pointerDrag.ColumnId;
            pointerDrag.End();
            e.Pointer.Capture(null);

            if (wasDragging)
            {
                var targetIndex = GetDetailsHeaderDropIndex(
                    headerCells.Select(static cell => cell.Bounds.Right).ToList(),
                    e.GetPosition(grid).X,
                    leadingRequiredCount);
                if (targetIndex != sourceIndex)
                {
                    DetailsColumnReordered?.Invoke(this, new DetailsColumnReorderedEventArgs(columnId, targetIndex));
                }

                e.Handled = true;
            }
        };

        grid.PointerCaptureLost += (_, _) =>
        {
            grid.ClearValue(CursorProperty);
            pointerDrag.End();
        };

        if (detailsColumns.Count > 0)
        {
            var fillerBorder = new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 24
            };
            fillerBorder.Bind(Border.BackgroundProperty, new DynamicResourceExtension("ThemeControlBackgroundBrush"));
            fillerBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeBorderLowBrush"));
            Grid.SetColumn(fillerBorder, detailsColumns.Count);
            grid.Children.Add(fillerBorder);
        }

        return grid;
    }

    private IDataTemplate BuildDetailsRowTemplate(IReadOnlyList<BrowserDetailsColumn> detailsColumns)
    {
        return new FuncDataTemplate<object?>((item, _) =>
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 1),
                ColumnDefinitions = BuildDetailsColumnDefinitions(detailsColumns, isHeader: false)
            };

            var col0Panel = new DockPanel
            {
                ClipToBounds = true,
                Margin = new Thickness(4, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var iconWithIndicator = BuildIconWithStatusIndicator(16, 16);
            DockPanel.SetDock(iconWithIndicator, Dock.Left);
            col0Panel.Children.Add(iconWithIndicator);

            var firstCol = detailsColumns.Count > 0 ? detailsColumns[0] : null;
            var valuePath = firstCol?.ValuePath ?? "Name";
            var valueAlignment = firstCol?.ValueAlignment ?? HorizontalAlignment.Left;

            var firstText = new TextBlock
            {
                HorizontalAlignment = valueAlignment,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(4, 0, 0, 0),
                ClipToBounds = true
            };
            if (valuePath == "DisplayType")
            {
                firstText.Bind(TextBlock.TextProperty, SkyCD.UI.Localization.DisplayTypeBinding.Create());
            }
            else
            {
                firstText.Bind(TextBlock.TextProperty, new Binding(valuePath));
            }
            col0Panel.Children.Add(firstText);
            Grid.SetColumn(col0Panel, 0);
            grid.Children.Add(col0Panel);

            for (var i = 1; i < detailsColumns.Count; i++)
            {
                var column = detailsColumns[i];
                var valueCell = new TextBlock
                {
                    HorizontalAlignment = column.ValueAlignment,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(6, 0),
                    ClipToBounds = true
                };
                if (column.ValuePath == "DisplayType")
                {
                    valueCell.Bind(TextBlock.TextProperty, SkyCD.UI.Localization.DisplayTypeBinding.Create());
                }
                else
                {
                    valueCell.Bind(TextBlock.TextProperty, new Binding(column.ValuePath));
                }
                Grid.SetColumn(valueCell, i);
                grid.Children.Add(valueCell);
            }

            return grid;
        });
    }

    private Control BuildIconWithStatusIndicator(
        double iconWidth,
        double iconHeight,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
    {
        var indicatorSize = Math.Clamp(Math.Min(iconWidth, iconHeight) * 0.5, 10, 16);

        var iconContainer = new Grid
        {
            Width = iconWidth,
            Height = iconHeight,
            HorizontalAlignment = horizontalAlignment,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new Image { Width = iconWidth, Height = iconHeight, HorizontalAlignment = horizontalAlignment };
        icon.Bind(Image.SourceProperty, new Binding("IconGlyph") { Converter = IconConverter });
        iconContainer.Children.Add(icon);

        // Added only when a thumbnail source exists, so icon-only rendering keeps its exact former shape.
        if (ThumbnailProvider is { } thumbnailProvider)
        {
            var thumbnail = new AsyncThumbnailImage
            {
                Width = iconWidth,
                Height = iconHeight,
                HorizontalAlignment = horizontalAlignment,
                Stretch = Stretch.UniformToFill,
                ThumbnailProvider = thumbnailProvider
            };
            thumbnail.Bind(AutomationProperties.NameProperty, PreviewAutomationBinding.CreateThumbnailName());
            iconContainer.Children.Add(thumbnail);
        }

        var statusIcon = new ContentControl
        {
            Width = indicatorSize - 2,
            Height = indicatorSize - 2,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        statusIcon.Bind(ContentControl.ContentProperty, new Binding($"Properties[{ItemStatusIconGlyphPropertyKey}]")
        {
            Converter = StatusIconConverter,
            FallbackValue = null
        });
        statusIcon.Bind(ContentControl.ForegroundProperty, new Binding($"Properties[{ItemStatusIconColorPropertyKey}]")
        {
            Converter = BrushFromStringConverter
        });
        var badge = new Border
        {
            Width = indicatorSize,
            Height = indicatorSize,
            Padding = new Thickness(1),
            CornerRadius = new CornerRadius(indicatorSize / 2),
            Background = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = statusIcon
        };
        badge.Bind(IsVisibleProperty, new Binding($"Properties[{ItemStatusIconGlyphPropertyKey}]")
        {
            Converter = HasNonEmptyValueConverter,
            FallbackValue = false
        });
        badge.Bind(ToolTip.TipProperty, new Binding("Properties[StatusName]"));
        iconContainer.Children.Add(badge);

        var loanBadge = new TextBlock
        {
            Text = "↗",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            FontSize = 10
        };
        loanBadge.Bind(IsVisibleProperty, new Binding("IsLent") { FallbackValue = false });
        loanBadge.Bind(ToolTip.TipProperty, new Binding("LentTo"));
        iconContainer.Children.Add(loanBadge);

        return iconContainer;
    }

    private const string ItemStatusIconGlyphPropertyKey = "StatusIconGlyph";
    private const string ItemStatusIconColorPropertyKey = "StatusIconColor";

    /// <summary>Horizontal travel, in pixels, before a header press turns into a reorder drag.</summary>
    private const double DetailsHeaderDragThreshold = 4;

    private void RaiseDetailsColumnResized(BrowserDetailsColumn column, bool widthChanged)
    {
        if (!widthChanged || string.IsNullOrEmpty(column.Id))
        {
            return;
        }

        var width = column.Width.GridUnitType == GridUnitType.Pixel ? column.Width.Value : 0;
        if (width <= 0)
        {
            return;
        }

        DetailsColumnResized?.Invoke(this, new DetailsColumnResizedEventArgs(column.Id, width));
    }

    /// <summary>
    /// Resolves the drop position for a dragged header: the index of the header cell under
    /// <paramref name="pointerX"/>, clamped to the first movable slot (the leading required column
    /// stays pinned) and to the last column.
    /// </summary>
    private static int GetDetailsHeaderDropIndex(
        IReadOnlyList<double> cellRightEdges,
        double pointerX,
        int firstMovableIndex)
    {
        if (cellRightEdges.Count == 0)
        {
            return 0;
        }

        var index = 0;
        while (index < cellRightEdges.Count - 1 && pointerX >= cellRightEdges[index])
        {
            index++;
        }

        return Math.Clamp(index, Math.Min(firstMovableIndex, cellRightEdges.Count - 1), cellRightEdges.Count - 1);
    }

    private sealed class DetailsHeaderPointerDrag
    {
        public bool IsActive { get; private set; }

        public bool IsDragging { get; set; }

        public IPointer? Pointer { get; private set; }

        public double StartX { get; private set; }

        public int SourceVisibleIndex { get; private set; }

        public string ColumnId { get; private set; } = string.Empty;

        public void Begin(IPointer pointer, double startX, int sourceVisibleIndex, string columnId)
        {
            IsActive = true;
            IsDragging = false;
            Pointer = pointer;
            StartX = startX;
            SourceVisibleIndex = sourceVisibleIndex;
            ColumnId = columnId;
        }

        public void End()
        {
            IsActive = false;
            IsDragging = false;
            Pointer = null;
            SourceVisibleIndex = -1;
            ColumnId = string.Empty;
        }
    }

    private static ColumnDefinitions BuildDetailsColumnDefinitions(
        IReadOnlyList<BrowserDetailsColumn> detailsColumns,
        bool isHeader = false)
    {
        var columns = new ColumnDefinitions();

        if (detailsColumns.Count == 0)
        {
            columns.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
            return columns;
        }

        foreach (var column in detailsColumns)
        {
            var colDef = new ColumnDefinition();
            colDef.Bind(ColumnDefinition.WidthProperty, new Binding(nameof(BrowserDetailsColumn.Width)) { Source = column });
            columns.Add(colDef);
        }

        if (isHeader)
        {
            columns.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        }

        return columns;
    }
}
