using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls;

public partial class PropertiesTabControl : UserControl
{
    public static readonly StyledProperty<bool> HasInfoTabProperty =
        AvaloniaProperty.Register<PropertiesTabControl, bool>(nameof(HasInfoTab));

    public static readonly StyledProperty<object?> GeneralContentProperty =
        AvaloniaProperty.Register<PropertiesTabControl, object?>(nameof(GeneralContent));

    public static readonly StyledProperty<object?> InfoContentProperty =
        AvaloniaProperty.Register<PropertiesTabControl, object?>(nameof(InfoContent));

    public PropertiesTabControl()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public bool HasInfoTab
    {
        get => GetValue(HasInfoTabProperty);
        set => SetValue(HasInfoTabProperty, value);
    }

    public object? GeneralContent
    {
        get => GetValue(GeneralContentProperty);
        set => SetValue(GeneralContentProperty, value);
    }

    public object? InfoContent
    {
        get => GetValue(InfoContentProperty);
        set => SetValue(InfoContentProperty, value);
    }
}
