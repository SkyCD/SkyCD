using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SkyCD.UI.Controls.StatusBar;

public partial class StatusBar : UserControl
{
    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<StatusBar, string?>(nameof(StatusText));

    public static readonly StyledProperty<string?> ProgressTextProperty =
        AvaloniaProperty.Register<StatusBar, string?>(nameof(ProgressText));

    public static readonly StyledProperty<bool> IsProgressVisibleProperty =
        AvaloniaProperty.Register<StatusBar, bool>(nameof(IsProgressVisible));

    public static readonly StyledProperty<double> ProgressValueProperty =
        AvaloniaProperty.Register<StatusBar, double>(nameof(ProgressValue));

    public static readonly StyledProperty<double> ProgressMinProperty =
        AvaloniaProperty.Register<StatusBar, double>(nameof(ProgressMin), 0);

    public static readonly StyledProperty<double> ProgressMaxProperty =
        AvaloniaProperty.Register<StatusBar, double>(nameof(ProgressMax), 100);

    public StatusBar()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public string? StatusText
    {
        get => GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string? ProgressText
    {
        get => GetValue(ProgressTextProperty);
        set => SetValue(ProgressTextProperty, value);
    }

    public bool IsProgressVisible
    {
        get => GetValue(IsProgressVisibleProperty);
        set => SetValue(IsProgressVisibleProperty, value);
    }

    public double ProgressValue
    {
        get => GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public double ProgressMin
    {
        get => GetValue(ProgressMinProperty);
        set => SetValue(ProgressMinProperty, value);
    }

    public double ProgressMax
    {
        get => GetValue(ProgressMaxProperty);
        set => SetValue(ProgressMaxProperty, value);
    }
}