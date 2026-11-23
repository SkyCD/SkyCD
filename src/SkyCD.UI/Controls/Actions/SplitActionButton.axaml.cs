using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SkyCD.UI.Controls.Actions;

/// <summary>
/// Primary action button with an optional dropdown of secondary actions. The dropdown part is hidden entirely
/// when no secondary action applies, so a state without a secondary action never shows a dead chevron.
/// </summary>
public partial class SplitActionButton : UserControl
{
    public static readonly StyledProperty<object?> PrimaryContentProperty =
        AvaloniaProperty.Register<SplitActionButton, object?>(nameof(PrimaryContent));

    public static readonly StyledProperty<ICommand?> PrimaryCommandProperty =
        AvaloniaProperty.Register<SplitActionButton, ICommand?>(nameof(PrimaryCommand));

    public static readonly StyledProperty<object?> PrimaryCommandParameterProperty =
        AvaloniaProperty.Register<SplitActionButton, object?>(nameof(PrimaryCommandParameter));

    public static readonly StyledProperty<bool> IsPrimaryEnabledProperty =
        AvaloniaProperty.Register<SplitActionButton, bool>(nameof(IsPrimaryEnabled), defaultValue: true);

    public static readonly StyledProperty<string?> PrimaryToolTipProperty =
        AvaloniaProperty.Register<SplitActionButton, string?>(nameof(PrimaryToolTip));

    public static readonly StyledProperty<bool> IsSecondaryVisibleProperty =
        AvaloniaProperty.Register<SplitActionButton, bool>(nameof(IsSecondaryVisible));

    public static readonly StyledProperty<bool> IsSecondaryEnabledProperty =
        AvaloniaProperty.Register<SplitActionButton, bool>(nameof(IsSecondaryEnabled), defaultValue: true);

    public static readonly StyledProperty<object?> SecondaryContentProperty =
        AvaloniaProperty.Register<SplitActionButton, object?>(nameof(SecondaryContent));

    public static readonly StyledProperty<ICommand?> SecondaryCommandProperty =
        AvaloniaProperty.Register<SplitActionButton, ICommand?>(nameof(SecondaryCommand));

    public static readonly StyledProperty<object?> SecondaryCommandParameterProperty =
        AvaloniaProperty.Register<SplitActionButton, object?>(nameof(SecondaryCommandParameter));

    public static readonly StyledProperty<string?> SecondaryToolTipProperty =
        AvaloniaProperty.Register<SplitActionButton, string?>(nameof(SecondaryToolTip));

    public SplitActionButton()
    {
        InitializeComponent();
    }

    public object? PrimaryContent
    {
        get => GetValue(PrimaryContentProperty);
        set => SetValue(PrimaryContentProperty, value);
    }

    public ICommand? PrimaryCommand
    {
        get => GetValue(PrimaryCommandProperty);
        set => SetValue(PrimaryCommandProperty, value);
    }

    public object? PrimaryCommandParameter
    {
        get => GetValue(PrimaryCommandParameterProperty);
        set => SetValue(PrimaryCommandParameterProperty, value);
    }

    public bool IsPrimaryEnabled
    {
        get => GetValue(IsPrimaryEnabledProperty);
        set => SetValue(IsPrimaryEnabledProperty, value);
    }

    public string? PrimaryToolTip
    {
        get => GetValue(PrimaryToolTipProperty);
        set => SetValue(PrimaryToolTipProperty, value);
    }

    public bool IsSecondaryVisible
    {
        get => GetValue(IsSecondaryVisibleProperty);
        set => SetValue(IsSecondaryVisibleProperty, value);
    }

    public bool IsSecondaryEnabled
    {
        get => GetValue(IsSecondaryEnabledProperty);
        set => SetValue(IsSecondaryEnabledProperty, value);
    }

    public object? SecondaryContent
    {
        get => GetValue(SecondaryContentProperty);
        set => SetValue(SecondaryContentProperty, value);
    }

    public ICommand? SecondaryCommand
    {
        get => GetValue(SecondaryCommandProperty);
        set => SetValue(SecondaryCommandProperty, value);
    }

    public object? SecondaryCommandParameter
    {
        get => GetValue(SecondaryCommandParameterProperty);
        set => SetValue(SecondaryCommandParameterProperty, value);
    }

    public string? SecondaryToolTip
    {
        get => GetValue(SecondaryToolTipProperty);
        set => SetValue(SecondaryToolTipProperty, value);
    }

    private void OnSecondaryActionClick(object? sender, RoutedEventArgs e)
    {
        if (!IsSecondaryVisible || SecondaryCommand is not { } command)
        {
            return;
        }

        var menu = BuildSecondaryMenu();
        if (menu.Items.Count > 0)
        {
            SecondaryActionButton.Flyout = menu;
            menu.ShowAt(SecondaryActionButton);
        }
    }

    private MenuFlyout BuildSecondaryMenu()
    {
        var menu = new MenuFlyout();
        var item = new MenuItem
        {
            Header = SecondaryContent,
            Command = SecondaryCommand,
            CommandParameter = SecondaryCommandParameter
        };
        if (!string.IsNullOrWhiteSpace(SecondaryToolTip))
        {
            AutomationProperties.SetName(item, SecondaryToolTip);
        }

        menu.Items.Add(item);
        return menu;
    }
}
