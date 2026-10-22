namespace SkyCD.Presentation.ViewModels;

public sealed class StatusMenuIcon
{
    public StatusMenuIcon(string iconGlyph, string? iconColor)
    {
        IconGlyph = iconGlyph;
        IconColor = iconColor;
    }

    public string IconGlyph { get; }
    public string? IconColor { get; }
}
