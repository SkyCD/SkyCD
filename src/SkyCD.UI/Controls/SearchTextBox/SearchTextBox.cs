using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using IconPacks.Avalonia;
using IconPacks.Avalonia.Lucide;

namespace SkyCD.UI.Controls.SearchTextBox;

/// <summary>
/// Issue #1184: every search field shares one presentation. A <see cref="TextBox"/> that draws the
/// Lucide search glyph in <see cref="TextBox.InnerLeftContent"/>, so the About assemblies filter,
/// the Options plugin filter and every other search field show the magnifier at an identical size,
/// gap and foreground. Bindings, placeholders and automation names behave exactly like a plain
/// <see cref="TextBox"/> because this is one.
/// </summary>
public sealed class SearchTextBox : TextBox
{
    // Inherit the TextBox look (implicit style and theme template): without this the control
    // would neither take the shared input padding nor realize any template to host the icon.
    protected override Type StyleKeyOverride => typeof(TextBox);

    public SearchTextBox()
    {
        var icon = new PackIconControl { Kind = PackIconLucideKind.Search };
        icon.Bind(Layoutable.WidthProperty, new DynamicResourceExtension("SkySearchIconSize"));
        icon.Bind(Layoutable.HeightProperty, new DynamicResourceExtension("SkySearchIconSize"));
        icon.Bind(Layoutable.MarginProperty, new DynamicResourceExtension("SkySearchIconMargin"));
        icon.Bind(PackIconControl.ForegroundProperty, new DynamicResourceExtension("ThemeForegroundLowBrush"));
        InnerLeftContent = icon;
    }
}
