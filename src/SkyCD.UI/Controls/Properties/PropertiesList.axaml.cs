using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Markup.Xaml;

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

    public PropertiesList()
    {
        PropertiesRows = [];
        Rows = PropertiesRows;
        AvaloniaXamlLoader.Load(this);
        RowsPanel = (ITemplate<Panel?>)Resources["DefaultRowsPanel"]!;
        PropertyCellTemplate = (IDataTemplate)Resources["DefaultPropertyCellTemplate"]!;
        ValueCellTemplate = (IDataTemplate)Resources["DefaultValueCellTemplate"]!;
    }

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

    public AvaloniaList<PropertiesRow> PropertiesRows { get; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PropertiesDataProperty)
        {
            RebuildRows(change.GetNewValue<IReadOnlyDictionary<string, object?>?>());
        }
    }

    private void RebuildRows(IReadOnlyDictionary<string, object?>? properties)
    {
        PropertiesRows.Clear();
        if (properties is null || properties.Count == 0)
        {
            return;
        }

        foreach (var (key, value) in properties)
        {
            PropertiesRows.Add(new PropertiesRow(key, value?.ToString() ?? string.Empty));
        }
    }
}

public sealed record PropertiesRow(string Key, string Value);