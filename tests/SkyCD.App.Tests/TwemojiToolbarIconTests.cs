using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;
using SkyCD.App.Icons;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1022: every shipped toolbar command paints colored Twemoji artwork through the solid-fill pipeline
/// the language flags use, and a key with no emoji equivalent keeps its monochrome glyph.
/// </summary>
public sealed class TwemojiToolbarIconTests : IDisposable
{
    public TwemojiToolbarIconTests() => ClearSeams();

    public void Dispose() => ClearSeams();

    private static void ClearSeams()
    {
        TwemojiToolbarIcons.LoaderOverride = null;
        ToolbarIconResolver.ArtworkLoaderOverride = null;
        ToolbarIconResolver.GlyphLoaderOverride = null;
        ToolbarIconResolver.AssetLoaderOverride = null;
    }

    [Fact]
    public void EveryShippedToolbarKey_HasColoredArtwork()
    {
        var mapped = new HashSet<string>(TwemojiToolbarIcons.Keys, StringComparer.Ordinal);

        var unmapped = ShippedToolbarKeys().Where(key => !mapped.Contains(key)).ToArray();

        Assert.Empty(unmapped);
    }

    [Fact]
    public void ColoredArtwork_MapsNoUnknownKey()
    {
        var shipped = new HashSet<string>(ShippedToolbarKeys(), StringComparer.Ordinal);

        var extra = TwemojiToolbarIcons.Keys.Where(key => !shipped.Contains(key)).ToArray();

        Assert.Empty(extra);
    }

    /// <summary>
    /// The asset the running application loads, parsed through the shared pipeline: a mapped icon must be
    /// flat solid-filled artwork, or the toolbar would fail while rebuilding. A name that did not match the
    /// Twemoji member it stands for would not have been materialized, so the missing file fails here.
    /// </summary>
    [Fact]
    public void EveryMappedIcon_ShipsSolidFilledArtworkWithItsOwnColors()
    {
        var assets = ToolbarAssetDirectory();
        var materialized = Directory.EnumerateFiles(assets, "*.svg")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var key in TwemojiToolbarIcons.Keys)
        {
            var name = TwemojiToolbarIcons.IconNameFor(key);

            Assert.True(materialized.Contains(name), $"Twemoji asset '{name}' for '{key}' was not materialized in {assets}.");

            var layers = TwemojiArtwork.ReadLayers(XDocument.Load(Path.Combine(assets, name + ".svg")).Root!, name);

            Assert.NotEmpty(layers);
            Assert.All(layers, layer =>
            {
                Assert.Matches("^#[0-9a-fA-F]{3,8}$", layer.Fill);
                Assert.False(string.IsNullOrWhiteSpace(layer.PathData));
                Assert.True(Color.Parse(layer.Fill).A > 0, $"Layer fill '{layer.Fill}' paints nothing.");
            });
        }
    }

    [Fact]
    public void ToolbarArtwork_PaintsWithTheEmojiPaletteNotOneFlatColor()
    {
        var assets = ToolbarAssetDirectory();

        var fills = TwemojiToolbarIcons.Keys
            .SelectMany(key => ShippedLayers(assets, TwemojiToolbarIcons.IconNameFor(key)))
            .Select(layer => layer.Fill)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.True(
            fills.Length >= 10,
            $"The toolbar artwork is expected to be colorful, but paints only {fills.Length} distinct colors.");
    }

    [Fact]
    public void MappedIcons_AreDistinctArtwork()
    {
        var names = TwemojiToolbarIcons.Keys.Select(TwemojiToolbarIcons.IconNameFor).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Artwork_ReadsCircleAndEllipseAsFilledLayers()
    {
        var svg = XElement.Parse(
            """<svg xmlns="http://www.w3.org/2000/svg" width="36" height="36"><circle cx="18" cy="18" r="9" fill="#5dadec"/><ellipse cx="10" cy="20" rx="4" ry="6" fill="#dd2e44"/></svg>""");

        var layers = TwemojiArtwork.ReadLayers(svg, "probe");

        Assert.Equal(2, layers.Count);
        Assert.Equal(["#5dadec", "#dd2e44"], layers.Select(layer => layer.Fill));
        Assert.All(layers, layer => Assert.StartsWith("M ", layer.PathData, StringComparison.Ordinal));
        Assert.Contains("a 9 9 0 1 0 18 0", layers[0].PathData, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><g><path fill="#5dadec" d="M0 0h6v6z"/></g></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><path d="M0 0h6v6z"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><path fill="currentColor" d="M0 0h6v6z"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><path fill="#5dadec"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><rect fill="#5dadec" width="6" height="6"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><circle cx="wide" cy="4" r="3" fill="#5dadec"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"></svg>""")]
    public void Artwork_RejectsAnythingTheFlatPipelineCannotPaint(string markup)
    {
        var exception = Assert.Throws<UnsupportedTwemojiArtworkException>(
            () => TwemojiArtwork.ReadLayers(XElement.Parse(markup), "probe"));

        Assert.Contains("probe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_PrefersColoredArtworkOverTheThemePaintedGlyph()
    {
        var colored = new StubImage();
        var glyphRequests = new List<string>();
        ToolbarIconResolver.ArtworkLoaderOverride = _ => colored;
        ToolbarIconResolver.GlyphLoaderOverride = key =>
        {
            glyphRequests.Add(key);
            return new StubImage();
        };

        var image = ToolbarIconResolver.Resolve(ToolbarIconKeys.Undo);

        Assert.Same(colored, image);
        Assert.Empty(glyphRequests);
    }

    [Fact]
    public void Resolver_FallsBackToTheMonochromeGlyphForAnUnmappedKey()
    {
        var glyph = new StubImage();
        var artworkRequests = new List<string>();
        var glyphRequests = new List<string>();
        ToolbarIconResolver.ArtworkLoaderOverride = key =>
        {
            artworkRequests.Add(key);
            return null;
        };
        ToolbarIconResolver.GlyphLoaderOverride = key =>
        {
            glyphRequests.Add(key);
            return glyph;
        };

        var image = ToolbarIconResolver.Resolve("ToolbarIcon.PluginCustom");

        Assert.Same(glyph, image);
        Assert.Equal(["ToolbarIcon.PluginCustom"], artworkRequests);
        Assert.Equal(["ToolbarIcon.PluginCustom"], glyphRequests);
    }

    private static IReadOnlyList<TwemojiArtwork.Layer> ShippedLayers(string assets, string name) =>
        TwemojiArtwork.ReadLayers(XDocument.Load(Path.Combine(assets, name + ".svg")).Root!, name);

    private static IEnumerable<string> ShippedToolbarKeys() =>
        typeof(ToolbarIconKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    private sealed class StubImage : IImage
    {
        public Size Size => new(16, 16);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }
    }

    private static string ToolbarAssetDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !directory.Name.Equals("Debug", StringComparison.OrdinalIgnoreCase)
            && !directory.Name.Equals("Release", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        var configuration = directory?.Name
            ?? throw new InvalidOperationException($"The test output {AppContext.BaseDirectory} is not under a Debug or Release build folder.");
        var assets = Path.Combine(
            FindRepoRoot(), "src", "SkyCD.App", "bin", configuration, "net10.0", "iconifybundle", "twemoji");

        Assert.True(Directory.Exists(assets), $"The Twemoji asset directory {assets} was not built.");
        return assets;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Repository root with SkyCD.slnx was not found.");
    }
}
