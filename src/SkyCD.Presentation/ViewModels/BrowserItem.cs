namespace SkyCD.Presentation.ViewModels;

public sealed record BrowserItem(string Name, string Type, string Size, string IconGlyph)
{
    public string Id { get; init; } = string.Empty;
}
