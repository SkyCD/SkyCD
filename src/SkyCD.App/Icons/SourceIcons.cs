using Avalonia.Media;
using IconifyBundle;

namespace SkyCD.App.Icons;

/// <summary>
/// The colored Twemoji artwork of the Add To List source selector (issue #1099), converted from the emoji by
/// the same <see cref="TwemojiArtwork"/> pipeline the toolbar icons and language flags use.
/// </summary>
public static class SourceIcons
{
    public static DrawingImage Media { get; } = TwemojiArtwork.Create(Twemoji.OpticalDisk);

    public static DrawingImage Folder { get; } = TwemojiArtwork.Create(Twemoji.FileFolder);

    public static DrawingImage Internet { get; } = TwemojiArtwork.Create(Twemoji.GlobeShowingEuropeAfrica);

    public static DrawingImage TitleOnly { get; } = TwemojiArtwork.Create(Twemoji.LetterT);

    public static DrawingImage Account { get; } = TwemojiArtwork.Create(Twemoji.BustInSilhouette);
}
