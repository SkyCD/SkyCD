using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace SkyCD.UI.Controls.Properties;

public partial class PropertiesList : UserControl
{
    public static readonly StyledProperty<IReadOnlyDictionary<string, object?>?> PropertiesDataProperty =
        AvaloniaProperty.Register<PropertiesList, IReadOnlyDictionary<string, object?>?>(nameof(PropertiesData));

    public static readonly StyledProperty<IEnumerable?> RowsProperty =
        AvaloniaProperty.Register<PropertiesList, IEnumerable?>(nameof(Rows));

    public static readonly StyledProperty<ITemplate<Panel?>?> RowsPanelProperty =
        AvaloniaProperty.Register<PropertiesList, ITemplate<Panel?>?>(nameof(RowsPanel));

    public static readonly StyledProperty<bool> RowIsTabStopProperty =
        AvaloniaProperty.Register<PropertiesList, bool>(nameof(RowIsTabStop), true);

    public static readonly StyledProperty<IDataTemplate?> PropertyCellTemplateProperty =
        AvaloniaProperty.Register<PropertiesList, IDataTemplate?>(nameof(PropertyCellTemplate));

    public static readonly StyledProperty<IDataTemplate?> ValueCellTemplateProperty =
        AvaloniaProperty.Register<PropertiesList, IDataTemplate?>(nameof(ValueCellTemplate));

    public static readonly StyledProperty<string?> PropertyHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string?>(nameof(PropertyHeader));

    public static readonly StyledProperty<string?> ValueHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string?>(nameof(ValueHeader));

    public static readonly StyledProperty<bool> EditValueInPlaceProperty =
        AvaloniaProperty.Register<PropertiesList, bool>(nameof(EditValueInPlace));

    /// <summary>
    /// Key of the single description row in <see cref="PropertiesData"/>; null when this list has none. The
    /// dialog sets its localized header, so the richer value surface follows the active language.
    /// </summary>
    public static readonly StyledProperty<string?> DescriptionRowHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string?>(nameof(DescriptionRowHeader));

    private const string ValueCellClass = "value-cell";

    public PropertiesList()
    {
        PropertiesRows = [];
        Rows = PropertiesRows;
        AvaloniaXamlLoader.Load(this);
        rowsListBox = this.FindControl<ListBox>("RowsListBox")
            ?? throw new InvalidOperationException("PropertiesList template is missing its rows list.");
        RowsPanel = (ITemplate<Panel?>)Resources["DefaultRowsPanel"]!;
        PropertyCellTemplate = (IDataTemplate)Resources["DefaultPropertyCellTemplate"]!;
        ValueCellTemplate = (IDataTemplate)Resources["DefaultValueCellTemplate"]!;
        rowsListBox.AddHandler(PointerPressedEvent, OnRowsPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        rowsListBox.AddHandler(PointerReleasedEvent, OnRowsPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        rowsListBox.AddHandler(PointerCaptureLostEvent, OnRowsPointerReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        rowsListBox.GotFocus += OnRowsGotFocus;
        rowsListBox.SelectionChanged += OnRowsSelectionChanged;
    }

    /// <summary>Raised when a row asks to edit its value cell; the host owns the editor lifecycle.</summary>
    public event EventHandler<PropertiesValueEditRequestedEventArgs>? ValueCellEditRequested;

    /// <summary>Raised after the grid selection changes, so the host can commit an editor left behind.</summary>
    public event EventHandler? RowSelectionChanged;

    /// <summary>The selected row item, or null when nothing is selected.</summary>
    public object? SelectedItem => rowsListBox.SelectedItem;

    public IReadOnlyDictionary<string, object?>? PropertiesData
    {
        get => GetValue(PropertiesDataProperty);
        set => SetValue(PropertiesDataProperty, value);
    }

    public IEnumerable? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public ITemplate<Panel?>? RowsPanel
    {
        get => GetValue(RowsPanelProperty);
        set => SetValue(RowsPanelProperty, value);
    }

    public bool RowIsTabStop
    {
        get => GetValue(RowIsTabStopProperty);
        set => SetValue(RowIsTabStopProperty, value);
    }

    public IDataTemplate? PropertyCellTemplate
    {
        get => GetValue(PropertyCellTemplateProperty);
        set => SetValue(PropertyCellTemplateProperty, value);
    }

    public IDataTemplate? ValueCellTemplate
    {
        get => GetValue(ValueCellTemplateProperty);
        set => SetValue(ValueCellTemplateProperty, value);
    }

    public string? PropertyHeader
    {
        get => GetValue(PropertyHeaderProperty);
        set => SetValue(PropertyHeaderProperty, value);
    }

    public string? ValueHeader
    {
        get => GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }

    /// <summary>Lets the value cell edit in place: a click on the cell or keyboard focus on its row opens the editor.</summary>
    public bool EditValueInPlace
    {
        get => GetValue(EditValueInPlaceProperty);
        set => SetValue(EditValueInPlaceProperty, value);
    }

    public string? DescriptionRowHeader
    {
        get => GetValue(DescriptionRowHeaderProperty);
        set => SetValue(DescriptionRowHeaderProperty, value);
    }

    public AvaloniaList<PropertiesRow> PropertiesRows { get; }

    /// <summary>The visible in-place editor of <paramref name="item"/>, or null when its row is not realized.</summary>
    public TextBox? FindValueEditor(object item) =>
        FindRow(item)?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(editor => editor.IsEffectivelyVisible);

    /// <summary>
    /// Selects the row of <paramref name="item"/> and gives it keyboard focus. With
    /// <paramref name="startEditing"/> the focused row opens its in-place editor.
    /// </summary>
    public void FocusRow(object item, bool startEditing = true)
    {
        rowsListBox.ScrollIntoView(item);
        Dispatcher.UIThread.Post(() =>
        {
            if (FindRow(item) is { } row)
            {
                suppressEditOnFocus = !startEditing;
                try
                {
                    rowsListBox.SelectedItem = item;
                    row.Focus();
                }
                finally
                {
                    suppressEditOnFocus = false;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Moves keyboard focus and in-place editing to the row <paramref name="direction"/> steps from
    /// <paramref name="item"/>. Returns false at the first or last row so the host can let Tab leave the grid.
    /// </summary>
    public bool MoveEdit(object item, int direction)
    {
        var items = Rows?.Cast<object?>().ToList() ?? [];
        var index = items.IndexOf(item);
        var target = index < 0 ? -1 : index + direction;
        if (target < 0 || target >= items.Count || items[target] is not { } next)
        {
            return false;
        }

        FocusRow(next);
        return true;
    }

    private readonly ListBox rowsListBox;
    private bool pointerPressed;
    private bool suppressEditOnFocus;

    private ListBoxItem? FindRow(object item) =>
        rowsListBox.GetVisualDescendants().OfType<ListBoxItem>()
            .FirstOrDefault(row => ReferenceEquals(row.DataContext, item));

    private void RequestValueEdit(object item) =>
        ValueCellEditRequested?.Invoke(this, new PropertiesValueEditRequestedEventArgs(item));

    private void OnRowsPointerReleased(object? sender, PointerReleasedEventArgs e) => pointerPressed = false;

    private void OnRowsSelectionChanged(object? sender, SelectionChangedEventArgs e) =>
        RowSelectionChanged?.Invoke(this, EventArgs.Empty);

    private void OnRowsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        pointerPressed = true;
        if (!EditValueInPlace || e.Source is not Visual source)
        {
            return;
        }

        var inValueCell = false;
        Visual? node = source;
        while (node is not null and not ListBoxItem)
        {
            // A click inside the open editor is caret placement, not a new edit request.
            if (node is TextBox)
            {
                return;
            }

            inValueCell |= node is Border border && border.Classes.Contains(ValueCellClass);
            node = node.GetVisualParent();
        }

        if (inValueCell && node is ListBoxItem { DataContext: { } item })
        {
            RequestValueEdit(item);
        }
    }

    private void OnRowsGotFocus(object? sender, RoutedEventArgs e)
    {
        if (!EditValueInPlace || pointerPressed || suppressEditOnFocus)
        {
            return;
        }

        switch (e.Source)
        {
            case ListBoxItem { DataContext: { } item }:
                RequestValueEdit(item);
                break;
            case ListBox:
                // Tabbing into the grid from outside starts at the selected row, or the first one.
                if (rowsListBox.SelectedItem is { } selected)
                {
                    FocusRow(selected);
                }
                else if (Rows?.Cast<object?>().FirstOrDefault() is { } first)
                {
                    FocusRow(first);
                }

                break;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PropertiesDataProperty || change.Property == DescriptionRowHeaderProperty)
        {
            RebuildRows(PropertiesData);
        }
    }

    private void RebuildRows(IReadOnlyDictionary<string, object?>? properties)
    {
        PropertiesRows.Clear();
        if (properties is null || properties.Count == 0)
        {
            return;
        }

        var descriptionHeader = DescriptionRowHeader;
        foreach (var (key, value) in properties)
        {
            var isDescriptionRow = descriptionHeader is not null && string.Equals(key, descriptionHeader, StringComparison.Ordinal);
            PropertiesRows.Add(new PropertiesRow(key, value?.ToString() ?? string.Empty, isDescriptionRow));
        }
    }
}

/// <summary>
/// One rendered key/value row. <paramref name="IsDescriptionRow"/> marks the entry's description so its value
/// cell can host the richer description surface while keeping the plain key/value text.
/// </summary>
public sealed record PropertiesRow(string Key, string Value, bool IsDescriptionRow = false);
