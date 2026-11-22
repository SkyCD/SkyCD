using Avalonia;

namespace SkyCD.Presentation.ViewModels;

/// <summary>One Options sidebar row: a non-selectable group header or a selectable category.</summary>
public sealed record OptionsSidebarEntry(string Title, OptionsSettingCategory? Category, int Depth = 0)
{
    public bool IsGroupHeader => Category is null;

    public Thickness RowMargin => IsGroupHeader
        ? new Thickness(8, 8, 8, 2)
        : new Thickness(Depth > 0 ? 24 : 8, 5, 8, 5);
}
