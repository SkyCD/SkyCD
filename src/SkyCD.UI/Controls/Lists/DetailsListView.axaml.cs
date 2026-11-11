using System;
using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls.Lists;

public partial class DetailsListView : UserControl
{
    private bool isUpdatingSelection;

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DetailsListView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<DetailsListView, object?>(nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IList?> SelectedItemsProperty =
        AvaloniaProperty.Register<DetailsListView, IList?>(nameof(SelectedItems),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<DetailsListView, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<IDataTemplate?> RowTemplateProperty =
        AvaloniaProperty.Register<DetailsListView, IDataTemplate?>(nameof(RowTemplate));

    public static readonly StyledProperty<ContextMenu?> ListContextMenuProperty =
        AvaloniaProperty.Register<DetailsListView, ContextMenu?>(nameof(ListContextMenu));

    public static readonly StyledProperty<double> ListMinWidthProperty =
        AvaloniaProperty.Register<DetailsListView, double>(nameof(ListMinWidth));

    public new event EventHandler<TappedEventArgs>? DoubleTapped;

    public DetailsListView()
    {
        AvaloniaXamlLoader.Load(this);
        var listBox = this.FindControl<ListBox>("InnerListBox");
        if (listBox != null)
        {
            listBox.DoubleTapped += (s, e) => DoubleTapped?.Invoke(this, e);
            listBox.SelectionChanged += (_, _) =>
            {
                if (isUpdatingSelection)
                {
                    return;
                }

                SelectedItem = listBox.SelectedItem;
                SelectedItems = listBox.SelectedItems?.Cast<object>().ToList();
            };
        }
    }

    public void ScrollIntoView(object? item)
    {
        if (item is null)
        {
            return;
        }

        var listBox = this.FindControl<ListBox>("InnerListBox");
        listBox?.ScrollIntoView(item);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SelectedItemProperty || change.Property == SelectedItemsProperty)
        {
            if (!isUpdatingSelection)
            {
                var listBox = this.FindControl<ListBox>("InnerListBox");
                if (listBox is not null)
                {
                    isUpdatingSelection = true;
                    try
                    {
                        if (SelectedItems is { Count: > 0 } && listBox.Selection is not null)
                        {
                            listBox.Selection.Clear();
                            var items = listBox.ItemsSource?.Cast<object>().ToList();
                            if (items is not null)
                            {
                                foreach (var sel in SelectedItems)
                                {
                                    if (sel is not null)
                                    {
                                        var idx = items.IndexOf(sel);
                                        if (idx >= 0)
                                        {
                                            listBox.Selection.Select(idx);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            listBox.SelectedItem = SelectedItem;
                        }
                    }
                    finally
                    {
                        isUpdatingSelection = false;
                    }
                }
            }
        }
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

    public object? HeaderContent
    {
        get => GetValue(HeaderContentProperty);
        set => SetValue(HeaderContentProperty, value);
    }

    public IDataTemplate? RowTemplate
    {
        get => GetValue(RowTemplateProperty);
        set => SetValue(RowTemplateProperty, value);
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
}
