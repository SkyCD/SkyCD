using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace SkyCD.UI.Controls.Lists;

public partial class DetailsListView : UserControl
{
    private bool isUpdatingSelection;
    private ScrollViewer? listScrollViewer;
    private string typeToSelectBuffer = string.Empty;
    private DateTime lastTypeToSelectTime = DateTime.MinValue;

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
            listBox.KeyDown += OnListBoxKeyDown;
            listBox.TextInput += OnListBoxTextInput;
            listBox.SelectionChanged += (_, _) =>
            {
                if (isUpdatingSelection)
                {
                    return;
                }

                isUpdatingSelection = true;
                try
                {
                    SelectedItem = listBox.SelectedItem;
                    SelectedItems = listBox.SelectedItems?.Cast<object>().ToList();
                }
                finally
                {
                    isUpdatingSelection = false;
                }
            };
        }
        Loaded += (_, _) => AttachScrollViewer();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        AttachScrollViewer();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        AttachScrollViewer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookScrollViewer();
    }

    private void AttachScrollViewer()
    {
        if (listScrollViewer is not null)
        {
            return;
        }

        var listBox = this.FindControl<ListBox>("InnerListBox");
        if (listBox is null)
        {
            return;
        }

        listBox.ApplyTemplate();
        listScrollViewer = listBox.FindDescendantOfType<ScrollViewer>() ??
                           listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (listScrollViewer is null)
        {
            EventHandler<TemplateAppliedEventArgs>? handler = null;
            handler = (_, _) =>
            {
                listScrollViewer = listBox.FindDescendantOfType<ScrollViewer>() ??
                                   listBox.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                HookScrollViewer();
                listBox.TemplateApplied -= handler;
            };
            listBox.TemplateApplied += handler;
        }
        else
        {
            HookScrollViewer();
        }
    }

    private void HookScrollViewer()
    {
        if (listScrollViewer is null)
        {
            return;
        }

        listScrollViewer.PropertyChanged += OnListScrollViewerPropertyChanged;
        listScrollViewer.ScrollChanged += OnListScrollViewerScrollChanged;
        UpdateHeaderLayout();
    }

    private void UnhookScrollViewer()
    {
        if (listScrollViewer is null)
        {
            return;
        }

        listScrollViewer.PropertyChanged -= OnListScrollViewerPropertyChanged;
        listScrollViewer.ScrollChanged -= OnListScrollViewerScrollChanged;
        listScrollViewer = null;
    }

    private void OnListScrollViewerPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ScrollViewer.OffsetProperty ||
            e.Property == ScrollViewer.ExtentProperty ||
            e.Property == ScrollViewer.ViewportProperty)
        {
            UpdateHeaderLayout();
        }
    }

    private void OnListScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        UpdateHeaderLayout();
    }

    private void UpdateHeaderLayout()
    {
        if (listScrollViewer is null)
        {
            return;
        }

        var headerScrollViewer = this.FindControl<ScrollViewer>("HeaderScrollViewer");
        if (headerScrollViewer is not null)
        {
            headerScrollViewer.Offset = new Vector(listScrollViewer.Offset.X, 0);
        }

        var headerSpacer = this.FindControl<Border>("HeaderScrollBarSpacer");
        if (headerSpacer is not null)
        {
            var isVerticalScrollBarVisible = listScrollViewer.Extent.Height > listScrollViewer.Viewport.Height;
            var scrollBars = listScrollViewer.GetVisualDescendants().OfType<ScrollBar>().ToList();
            var verticalScrollBar = scrollBars.FirstOrDefault(sb => sb.Orientation == Avalonia.Layout.Orientation.Vertical);
            var scrollBarWidth = verticalScrollBar?.Bounds.Width ?? 14;
            headerSpacer.Width = isVerticalScrollBarVisible ? Math.Max(10, scrollBarWidth) : 0;
        }
    }

    private void OnListBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var listBox = sender as ListBox;
            if (listBox?.ItemsSource is IEnumerable items)
            {
                var list = items.Cast<object>().ToList();
                listBox.Selection?.SelectAll();
                SelectedItem = list.LastOrDefault();
                SelectedItems = list;
                e.Handled = true;
            }
        }
    }

    private void OnListBoxTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        var listBox = sender as ListBox;
        if (listBox?.ItemsSource is null)
        {
            return;
        }

        var items = listBox.ItemsSource.Cast<object>().ToList();
        if (items.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if ((now - lastTypeToSelectTime).TotalMilliseconds > 1000)
        {
            typeToSelectBuffer = string.Empty;
        }

        lastTypeToSelectTime = now;
        typeToSelectBuffer += e.Text;

        var currentIdx = listBox.SelectedIndex;
        var matchIdx = -1;

        if (typeToSelectBuffer.Length > 1 && typeToSelectBuffer.All(c => char.ToUpperInvariant(c) == char.ToUpperInvariant(typeToSelectBuffer[0])))
        {
            var queryChar = typeToSelectBuffer[0].ToString();
            for (var i = 1; i <= items.Count; i++)
            {
                var checkIdx = (currentIdx + i) % items.Count;
                var name = GetItemName(items[checkIdx]);
                if (name.StartsWith(queryChar, StringComparison.CurrentCultureIgnoreCase))
                {
                    matchIdx = checkIdx;
                    break;
                }
            }
        }
        else
        {
            for (var i = 0; i < items.Count; i++)
            {
                var name = GetItemName(items[i]);
                if (name.StartsWith(typeToSelectBuffer, StringComparison.CurrentCultureIgnoreCase))
                {
                    matchIdx = i;
                    break;
                }
            }
        }

        if (matchIdx >= 0)
        {
            var matchedItem = items[matchIdx];
            isUpdatingSelection = true;
            try
            {
                listBox.SelectedIndex = matchIdx;
                SelectedItem = matchedItem;
                SelectedItems = new List<object> { matchedItem };
            }
            finally
            {
                isUpdatingSelection = false;
            }

            listBox.ScrollIntoView(matchedItem);
            e.Handled = true;
        }
    }

    private static string GetItemName(object? item)
    {
        if (item is null)
        {
            return string.Empty;
        }

        var prop = item.GetType().GetProperty("Name");
        if (prop != null)
        {
            var val = prop.GetValue(item)?.ToString();
            if (!string.IsNullOrEmpty(val))
            {
                return val;
            }
        }

        return item.ToString() ?? string.Empty;
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
                            var currentSelection = listBox.SelectedItems?.Cast<object>().ToHashSet();
                            var newSelection = SelectedItems.Cast<object>().Where(static x => x != null).ToHashSet();
                            if (currentSelection == null || !currentSelection.SetEquals(newSelection))
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
                        }
                        else if (SelectedItems is { Count: 0 })
                        {
                            listBox.Selection?.Clear();
                            listBox.SelectedItem = null;
                        }
                        else
                        {
                            if (!Equals(listBox.SelectedItem, SelectedItem))
                            {
                                listBox.SelectedItem = SelectedItem;
                            }
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
