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
using Avalonia.Media;

namespace SkyCD.UI.Controls.Lists;

public partial class BrowserItemsView : UserControl
{
    private readonly DetailsListView? detailsListView;
    private readonly ListBox? listModeListBox;
    private readonly ListBox? iconGridListBox;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<BrowserItemsView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<BrowserItemsView, object?>(nameof(SelectedItem),
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

        listModeListBox.DoubleTapped += (_, e) => DoubleTapped?.Invoke(this, e);
        listModeListBox.ContextRequested += (_, e) => ContextRequested?.Invoke(this, e);

        iconGridListBox.DoubleTapped += (_, e) => DoubleTapped?.Invoke(this, e);
        iconGridListBox.ContextRequested += (_, e) => ContextRequested?.Invoke(this, e);

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
        else if (change.Property == IconConverterProperty || change.Property == DetailsColumnsProperty)
        {
            UpdateListTemplate();
            UpdateIconGridTemplate();
            UpdateDetailsTemplate();
        }
        else if (change.Property == BrowserGridItemWidthProperty || change.Property == BrowserGridItemHeightProperty)
        {
            UpdateIconGridTemplate();
        }
    }

    private void UpdateViewMode()
    {
        if (detailsListView is null || listModeListBox is null || iconGridListBox is null)
        {
            return;
        }

        detailsListView.IsVisible = ViewMode == BrowserViewMode.Details;
        listModeListBox.IsVisible = ViewMode == BrowserViewMode.List;
        iconGridListBox.IsVisible =
            ViewMode is BrowserViewMode.Tiles or BrowserViewMode.SmallIcons or BrowserViewMode.LargeIcons;
        IsTilesMode = ViewMode == BrowserViewMode.Tiles;
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
            var stack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(4, 2)
            };

            stack.Children.Add(BuildIconWithStatusIndicator(14, 14));

            var name = new TextBlock { FontSize = 13 };
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
            stack.Children.Add(name);

            return stack;
        });
    }

    private void UpdateIconGridTemplate()
    {
        if (iconGridListBox is null)
        {
            return;
        }

        iconGridListBox.ItemTemplate = new FuncDataTemplate<object?>((item, _) =>
        {
            var border = new Border
            {
                Width = BrowserGridItemWidth,
                Height = BrowserGridItemHeight,
                Margin = new Thickness(6),
                Padding = new Thickness(8),
                BorderBrush = new SolidColorBrush(Color.Parse("#D0D0D0")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            var stack = new StackPanel();
            border.Child = stack;

            stack.Children.Add(BuildIconWithStatusIndicator(32, 32, HorizontalAlignment.Center));

            var name = new TextBlock { TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
            name.Bind(TextBlock.TextProperty, new Binding("Name"));
            stack.Children.Add(name);

            var details = new StackPanel
            {
                Margin = new Thickness(0, 4, 0, 0),
                IsVisible = IsTilesMode
            };
            var type = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            type.Bind(TextBlock.TextProperty, new Binding("DisplayType"));
            details.Children.Add(type);

            var size = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12 };
            size.Bind(TextBlock.TextProperty, new Binding("DisplaySize"));
            details.Children.Add(size);
            stack.Children.Add(details);

            return border;
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

        return new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(4, 2),
            Child = grid
        };
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
                    HorizontalAlignment = column.ValueAlignment
                };
                valueCell.Bind(TextBlock.TextProperty, new Binding(column.ValuePath));
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
        var indicatorSize = Math.Clamp(Math.Min(iconWidth, iconHeight) * 0.35, 4, 12);
        var indicatorRadius = indicatorSize / 2;

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

        iconContainer.Children.Add(new Border
        {
            Width = indicatorSize,
            Height = indicatorSize,
            CornerRadius = new CornerRadius(indicatorRadius),
            Background = new SolidColorBrush(Color.Parse("#3B82F6")),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        });

        return iconContainer;
    }

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
