using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls;

public partial class PropertiesList : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<PropertiesList, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<string> PropertyHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string>(nameof(PropertyHeader), "Property");

    public static readonly StyledProperty<string> ValueHeaderProperty =
        AvaloniaProperty.Register<PropertiesList, string>(nameof(ValueHeader), "Value");

    public PropertiesList()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string PropertyHeader
    {
        get => GetValue(PropertyHeaderProperty);
        set => SetValue(PropertyHeaderProperty, value);
    }

    public string ValueHeader
    {
        get => GetValue(ValueHeaderProperty);
        set => SetValue(ValueHeaderProperty, value);
    }
}
