using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls;

public partial class DetailsListView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<DetailsListView, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<object?> SelectedItemProperty =
        AvaloniaProperty.Register<DetailsListView, object?>(nameof(SelectedItem), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<object?> HeaderContentProperty =
        AvaloniaProperty.Register<DetailsListView, object?>(nameof(HeaderContent));

    public static readonly StyledProperty<IDataTemplate?> RowTemplateProperty =
        AvaloniaProperty.Register<DetailsListView, IDataTemplate?>(nameof(RowTemplate));

    public static readonly StyledProperty<ContextMenu?> ListContextMenuProperty =
        AvaloniaProperty.Register<DetailsListView, ContextMenu?>(nameof(ListContextMenu));

    public static readonly StyledProperty<double> ListMinWidthProperty =
        AvaloniaProperty.Register<DetailsListView, double>(nameof(ListMinWidth));

    public event EventHandler<TappedEventArgs>? DoubleTapped;

    public DetailsListView()
    {
        AvaloniaXamlLoader.Load(this);
        var listBox = this.FindControl<ListBox>("InnerListBox");
        if (listBox != null)
        {
            listBox.DoubleTapped += (s, e) => DoubleTapped?.Invoke(this, e);
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
