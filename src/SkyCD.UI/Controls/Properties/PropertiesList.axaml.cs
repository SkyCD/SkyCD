using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls;

public partial class PropertiesList : UserControl
{
    public static readonly StyledProperty<IReadOnlyDictionary<string, object?>?> PropertiesDataProperty =
        AvaloniaProperty.Register<PropertiesList, IReadOnlyDictionary<string, object?>?>(nameof(PropertiesData));

    public static readonly StyledProperty<string?> PropertyHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string?>(nameof(PropertyHeader));

    public static readonly StyledProperty<string?> ValueHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string?>(nameof(ValueHeader));

    public PropertiesList()
    {
        PropertiesRows = [];
        AvaloniaXamlLoader.Load(this);
    }

    public IReadOnlyDictionary<string, object?>? PropertiesData
    {
        get => GetValue(PropertiesDataProperty);
        set => SetValue(PropertiesDataProperty, value);
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
