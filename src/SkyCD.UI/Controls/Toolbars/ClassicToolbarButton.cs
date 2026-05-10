using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace SkyCD.UI.Controls.Toolbars;

public class ClassicToolbarButton : Button, IClassicToolbarItem
{
    public static readonly StyledProperty<IImage?> ImageSrcProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, IImage?>(nameof(ImageSrc));

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ClassicToolbarButton, string?>(nameof(Text));

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
}