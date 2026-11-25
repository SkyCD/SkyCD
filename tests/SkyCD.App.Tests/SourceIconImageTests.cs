using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Media;
using SkyCD.App.Icons;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1099: the Add To List source selector paints four distinct colored Twemoji icons through the
/// same solid-fill artwork pipeline the toolbar icons and language flags use.
/// </summary>
public sealed class SourceIconImageTests
{
    // The Twemoji assets SourceIcons.Media, .Folder, .Internet and .TitleOnly are built from.
    private static readonly string[] AssetNames =
    [
        "optical-disk",
        "file-folder",
        "globe-showing-europe-africa",
        "letter-t"
    ];

    /// <summary>
    /// The asset the running application loads, parsed through the shared pipeline: each source icon must
    /// ship solid-filled artwork whose palette differs from the other sources.
    /// </summary>
    [Fact]
    public void EverySource_ShipsSolidFilledArtworkWithItsOwnPalette()
    {
        var assets = TwemojiAssetDirectory();

        var palettes = AssetNames
            .Select(name => ShippedLayers(assets, name))
            .Select(layers => string.Join(',', layers.Select(layer => layer.Fill).Distinct(StringComparer.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Equal(AssetNames.Length, palettes.Distinct(StringComparer.Ordinal).Count());

        foreach (var name in AssetNames)
        {
            Assert.True(File.Exists(Path.Combine(assets, name + ".svg")),
                $"Twemoji asset '{name}' used by the Add To List source icons was not materialized in {assets}.");

            var layers = ShippedLayers(assets, name);

            Assert.NotEmpty(layers);
            Assert.All(layers, layer =>
            {
                Assert.Matches("^#[0-9a-fA-F]{3,8}$", layer.Fill);
                Assert.True(Color.Parse(layer.Fill).A > 0, $"Layer fill '{layer.Fill}' paints nothing.");
            });
        }
    }

    private static IReadOnlyList<TwemojiArtwork.Layer> ShippedLayers(string assets, string name) =>
        TwemojiArtwork.ReadLayers(XDocument.Load(Path.Combine(assets, name + ".svg")).Root!, name);

    private static string TwemojiAssetDirectory()
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
