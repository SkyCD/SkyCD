using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

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

    public IValueConverter? StatusIconConverter
    {
        get => GetValue(StatusIconConverterProperty);
        set => SetValue(StatusIconConverterProperty, value);
    }

    public new event EventHandler<TappedEventArgs>? DoubleTapped;
    public new event EventHandler<ContextRequestedEventArgs>? ContextRequested;

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
                if (e.Property == DetailsListView.SelectedItemProperty)
                {
                    SelectedItem = detailsListView.SelectedItem;
                }
                else if (e.Property == DetailsListView.SelectedItemsProperty)
                {
                    SelectedItems = detailsListView.SelectedItems;
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
                SelectedItem = listModeListBox.SelectedItem;
                SelectedItems = listModeListBox.SelectedItems?.Cast<object>().ToList();
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
                SelectedItem = iconGridListBox.SelectedItem;
                SelectedItems = iconGridListBox.SelectedItems?.Cast<object>().ToList();
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
        else if (change.Property == IconConverterProperty || change.Property == StatusIconConverterProperty || change.Property == DetailsColumnsProperty)
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
        else
        {
            listBox.SelectedItem = selectedItem;
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
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
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
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
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
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
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
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
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

    private static object BuildDetailsHeaderContent(IReadOnlyList<BrowserDetailsColumn> detailsColumns)
    {
        var grid = new Grid
        {
            ColumnDefinitions = BuildDetailsColumnDefinitions(detailsColumns)
        };

        for (var i = 0; i < detailsColumns.Count; i++)
        {
            var column = detailsColumns[i];
            var text = new TextBlock
            {
                Text = column.Header,
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
                HorizontalAlignment = column.HeaderAlignment
            };
            Grid.SetColumn(text, i + 1);
            grid.Children.Add(text);
        }

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 2),
            Child = grid
        };
        border.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("ThemeBorderLowBrush"));
        return border;
    }

    private IDataTemplate BuildDetailsRowTemplate(IReadOnlyList<BrowserDetailsColumn> detailsColumns)
    {
        return new FuncDataTemplate<object?>((item, _) =>
        {
            var grid = new Grid
            {
                Margin = new Thickness(4, 2),
                ColumnDefinitions = BuildDetailsColumnDefinitions(detailsColumns)
            };

            var iconWithIndicator = BuildIconWithStatusIndicator(16, 16);
            Grid.SetColumn(iconWithIndicator, 0);
            grid.Children.Add(iconWithIndicator);

            for (var i = 0; i < detailsColumns.Count; i++)
            {
                var column = detailsColumns[i];
                var valueCell = new TextBlock
                {
                    HorizontalAlignment = column.ValueAlignment,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                if (column.ValuePath == "DisplayType")
                {
                    valueCell.Bind(TextBlock.TextProperty, SkyCD.UI.Localization.DisplayTypeBinding.Create());
                }
                else
                {
                    valueCell.Bind(TextBlock.TextProperty, new Binding(column.ValuePath));
                }
                Grid.SetColumn(valueCell, i + 1);
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

        return iconContainer;
    }

    private const string ItemStatusIconGlyphPropertyKey = "StatusIconGlyph";
    private const string ItemStatusIconColorPropertyKey = "StatusIconColor";

    private static ColumnDefinitions BuildDetailsColumnDefinitions(IReadOnlyList<BrowserDetailsColumn> detailsColumns)
    {
        var columns = new ColumnDefinitions
        {
            new ColumnDefinition(new GridLength(24, GridUnitType.Pixel))
        };

        foreach (var column in detailsColumns)
        {
            columns.Add(new ColumnDefinition(column.Width));
        }

        return columns;
    }
}
