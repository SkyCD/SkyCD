using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Avalonia.Media;
using IconifyBundle;
using SkyCD.Presentation.ViewModels;

namespace SkyCD.App.Icons;

/// <summary>
/// The colored Twemoji artwork behind the <c>ToolbarIcon.*</c> keys with a fitting emoji (issue #1022).
/// A toolbar command names a key, so the main toolbar, the Tools &gt; Toolbar lists and any menu showing the
/// command all paint the same artwork through <see cref="ToolbarIconResolver"/>.
/// </summary>
/// <remarks>
/// A key missing here keeps the monochrome glyph of
/// <c>avares://SkyCD.App/Styles/ToolbarIcons.axaml</c> (issue #1140: the project-owned glyph reads better
/// than any emoji for views, panes and the add command), which is what a plugin command without an
/// equivalent emoji still resolves to.
/// </remarks>
internal static class TwemojiToolbarIcons
{
    // IconifyBundle materialises an asset for a strongly-typed member access it can see at compile time, so
    // every entry names a Twemoji member; a name looked up at runtime would ship no artwork at all. The
    // recorded name is the asset that member writes out, which lets a test read the shipped SVG without the
    // icon registry the application populates at startup.
    private static readonly IReadOnlyDictionary<string, (string Name, Func<Icon> Icon)> Artwork =
        new Dictionary<string, (string Name, Func<Icon> Icon)>(StringComparer.Ordinal)
        {
            [ToolbarIconKeys.NewCatalog] = ("page-facing-up", () => Twemoji.PageFacingUp),
            [ToolbarIconKeys.OpenCatalog] = ("open-file-folder", () => Twemoji.OpenFileFolder),
            [ToolbarIconKeys.SaveCatalog] = ("floppy-disk", () => Twemoji.FloppyDisk),
            [ToolbarIconKeys.SaveCatalogAs] = ("memo", () => Twemoji.Memo),
            // Issue #1140: the hook arrowheads were verified against the shipped SVG geometry —
            // RightArrowCurvingLeft draws ↩ (undo) and LeftArrowCurvingRight draws ↪ (redo).
            [ToolbarIconKeys.Undo] = ("right-arrow-curving-left", () => Twemoji.RightArrowCurvingLeft),
            [ToolbarIconKeys.Redo] = ("left-arrow-curving-right", () => Twemoji.LeftArrowCurvingRight),
            [ToolbarIconKeys.DeleteItem] = ("wastebasket", () => Twemoji.Wastebasket),
            [ToolbarIconKeys.Properties] = ("gear", () => Twemoji.Gear),
            [ToolbarIconKeys.ViewTiles] = ("framed-picture", () => Twemoji.FramedPicture),
            [ToolbarIconKeys.ViewDetails] = ("card-index-dividers", () => Twemoji.CardIndexDividers),
            [ToolbarIconKeys.PreviewPane] = ("crystal-ball", () => Twemoji.CrystalBall),
            [ToolbarIconKeys.Refresh] = ("clockwise-vertical-arrows", () => Twemoji.ClockwiseVerticalArrows),
            [ToolbarIconKeys.Find] = ("magnifying-glass-tilted-left", () => Twemoji.MagnifyingGlassTiltedLeft),
            [ToolbarIconKeys.AdvancedFind] = ("telescope", () => Twemoji.Telescope),
            [ToolbarIconKeys.Tags] = ("label", () => Twemoji.Label),
            [ToolbarIconKeys.Collections] = ("books", () => Twemoji.Books)
        };

    private static readonly ConcurrentDictionary<string, DrawingImage> Images = new(StringComparer.Ordinal);

    /// <summary>
    /// Seam for tests: return the colored image for a key, or null to take the monochrome fallback branch.
    /// </summary>
    internal static Func<string, DrawingImage?>? LoaderOverride { get; set; }

    /// <summary>Every toolbar key that paints colored artwork.</summary>
    internal static IEnumerable<string> Keys => Artwork.Keys;

    /// <summary>The Twemoji asset a toolbar key paints.</summary>
    internal static string IconNameFor(string key) => Artwork[key].Name;

    /// <summary>The colored image for a key, or null when the key has no colored artwork.</summary>
    internal static DrawingImage? Load(string key)
    {
        if (LoaderOverride is { } loader)
        {
            return loader(key);
        }

        if (Images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        if (!Artwork.TryGetValue(key, out var entry))
        {
            return null;
        }

        var image = TwemojiArtwork.Create(entry.Icon());
        Images[key] = image;
        return image;
    }
}
