using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SkyCD.UI.Controls.Toolbars;

public partial class ClassicToolbarButton : Button, IClassicToolbarItem
{
    public static readonly StyledProperty<IImage?> ImageSrcProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, IImage?>(nameof(ImageSrc));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, string?>(
            nameof(Text),
            coerce: (_, val) => NormalizeMnemonic(val));

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