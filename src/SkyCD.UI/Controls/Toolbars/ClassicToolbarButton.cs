using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Media;

namespace SkyCD.UI.Controls.Toolbars;

[PseudoClasses(":checked")]
public partial class ClassicToolbarButton : Button, IClassicToolbarItem
{
    public static readonly StyledProperty<IImage?> ImageSrcProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, IImage?>(nameof(ImageSrc));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, string?>(
            nameof(Text),
            coerce: (_, val) => NormalizeMnemonic(val));

    /// <summary>
    /// Marks the command as a checkable view toggle. Only toggle commands expose the automation toggle
    /// pattern; regular commands stay invoke-only.
    /// </summary>
    public static readonly StyledProperty<bool> IsToggleProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, bool>(nameof(IsToggle));

    /// <summary>The current checked state of a toggle command, mirroring the view model state.</summary>
    public static readonly StyledProperty<bool> IsCheckedProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, bool>(nameof(IsChecked));

    /// <summary>Stable command id, so a live state change can find the rendered button without rebuilding.</summary>
    public static readonly StyledProperty<string?> CommandIdProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, string?>(nameof(CommandId));

    public ClassicToolbarButton()
    {
        UpdatePseudoClasses(IsChecked);
    }

    public IImage? ImageSrc
    {
        get => GetValue(ImageSrcProperty);
        set => SetValue(ImageSrcProperty, value);
    }

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsToggle
    {
        get => GetValue(IsToggleProperty);
        set => SetValue(IsToggleProperty, value);
    }

    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public string? CommandId
    {
        get => GetValue(CommandIdProperty);
        set => SetValue(CommandIdProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty)
        {
            UpdatePseudoClasses(change.GetNewValue<bool>());
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ClassicToolbarButtonAutomationPeer(this);

    private void UpdatePseudoClasses(bool isChecked) => PseudoClasses.Set(":checked", isChecked);

    private static string? NormalizeMnemonic(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // If text contains WinForms ampersand mnemonic (e.g., "&New"), convert it to Avalonia underscore mnemonic ("_New")
        if (text.Contains('&') && !text.Contains('_'))
        {
            return MnemonicRegex().Replace(text, "_").Replace("&&", "&");
        }

        return text;
    }

    [GeneratedRegex(@"(?<!&)&(?!&)")]
    private static partial Regex MnemonicRegex();
}
