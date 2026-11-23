using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;

using SkyCD.App.Converters;
using SkyCD.App.Icons;
using SkyCD.Documents;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #933: every toolbar command carries a project-owned vector glyph, the toolbar and the Options
/// lists resolve it through one source, and no command silently falls back to a text-only button.
/// </summary>
public sealed class ToolbarIconsTests : IDisposable
{
    private static readonly string GlyphDictionaryFile =
        Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Styles", "ToolbarIcons.axaml");

    public ToolbarIconsTests()
    {
        ToolbarIconResolver.GlyphLoaderOverride = null;
        ToolbarIconResolver.AssetLoaderOverride = null;
    }

    public void Dispose()
    {
        ToolbarIconResolver.GlyphLoaderOverride = null;
        ToolbarIconResolver.AssetLoaderOverride = null;
    }

    [Fact]
    public void EveryToolbarCommand_HasANonEmptyIcon()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());

        var commands = vm.ToolbarCommands();

        Assert.Equal(ToolbarCommandIds.All.Order(), commands.Select(command => command.Id).Order());
        Assert.All(commands, command =>
        {
            Assert.False(string.IsNullOrWhiteSpace(command.IconPath), $"Toolbar command '{command.Id}' has no icon.");
            Assert.StartsWith("ToolbarIcon.", command.IconPath, StringComparison.Ordinal);
        });
        Assert.Empty(commands.Where(command => command.IconPath is null).Select(command => command.Id));
    }

    [Fact]
    public void EveryToolbarCommand_IconIsDefinedInTheGlyphDictionary()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var glyphKeys = ReadGlyphDictionary();

        var missing = vm.ToolbarCommands()
            .Where(command => command.IconPath is null || !glyphKeys.ContainsKey(command.IconPath))
            .Select(command => command.Id)
            .ToArray();

        Assert.Empty(missing);
        Assert.Equal(ToolbarCommandIds.All.Count, vm.ToolbarCommands().Select(command => command.IconPath).Distinct().Count());
    }

    [Fact]
    public void GlyphDictionary_ShipsOneCrispGlyphPerCommandWithoutAdsOrDuplicates()
    {
        var glyphs = ReadGlyphDictionary();

        Assert.Equal(ToolbarCommandIds.All.Count, glyphs.Count);
        Assert.Equal(glyphs.Count, glyphs.Values.Distinct(StringComparer.Ordinal).Count());
        foreach (var (key, path) in glyphs)
        {
            var numbers = System.Text.RegularExpressions.Regex
                .Matches(path, @"-?\d+(?:\.\d+)?")
                .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture))
                .ToArray();

            // Every coordinate stays on the 16px slot: no negative leads and nothing past the right/bottom edge.
            Assert.NotEmpty(numbers);
            Assert.All(numbers, value => Assert.InRange(value, 0, GlyphSize));
        }
    }

    [Fact]
    public void OptionsLists_ExposeTheCommandIconAndKeepSeparatorsPlain()
    {
        using var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        var dialog = new ToolbarCustomizationViewModel(vm.ToolbarCommands(), ToolbarLayout.CreateDefault());

        Assert.All(dialog.AvailableCommands, choice => Assert.False(string.IsNullOrWhiteSpace(choice.IconPath)));
        Assert.All(
            dialog.SelectedItems.Where(item => !item.IsSeparator),
            item => Assert.False(string.IsNullOrWhiteSpace(item.IconPath)));
        Assert.Null(Assert.Single(dialog.SelectedItems, item => item.IsSeparator).IconPath);
    }

    [Fact]
    public void ToolbarIconConverter_ResolvesCommandsAndLeavesSeparatorsWithoutAnImage()
    {
        var converter = new ToolbarIconConverter();
        var requested = new System.Collections.Generic.List<string>();
        ToolbarIconResolver.GlyphLoaderOverride = key =>
        {
            requested.Add(key);
            return CreateGlyphImage();
        };

        var image = converter.Convert("ToolbarIcon.Undo", typeof(object), null, CultureInfo.InvariantCulture);
        var separator = converter.Convert(null, typeof(object), null, CultureInfo.InvariantCulture);

        Assert.IsAssignableFrom<IImage>(image);
        Assert.Null(separator);
        Assert.Equal(["ToolbarIcon.Undo"], requested);
    }

    [Fact]
    public void Resolver_KeepsAssetPathsOnTheBitmapPath()
    {
        var assetRequests = new System.Collections.Generic.List<string>();
        ToolbarIconResolver.GlyphLoaderOverride = key =>
            throw new InvalidOperationException($"Unexpected glyph lookup for '{key}'.");
        ToolbarIconResolver.AssetLoaderOverride = path =>
        {
            assetRequests.Add(path);
            return CreateGlyphImage();
        };

        var first = ToolbarIconResolver.Resolve("/Assets/new.png");
        var second = ToolbarIconResolver.Resolve("/Assets/new.png");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(["/Assets/new.png"], assetRequests);
    }

    [Fact]
    public void Resolver_FailsLoudlyForAnUnknownIcon()
    {
        ToolbarIconResolver.GlyphLoaderOverride = key =>
            key == "ToolbarIcon.Undo" ? CreateGlyphImage() : null;

        var exception = Assert.Throws<InvalidOperationException>(() => ToolbarIconResolver.Resolve("ToolbarIcon.Missing"));

        Assert.Contains("ToolbarIcon.Missing", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ToolbarIconResolver.GlyphDictionaryPath, exception.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ToolbarIconResolver.Resolve("Assets/not-a-path"));
    }

    [Fact]
    public void DesignTokens_IncludeTheSharedIconSlot()
    {
        // The toolbar template and both Options lists read the same slot instead of ad-hoc 16s.
        var tokens = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Styles", "DesignTokens.axaml"));
        var styles = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "SkyCD.App", "Styles", "ButtonStyles.axaml"));

        Assert.Contains("<x:Double x:Key=\"SkyIconSize\">16</x:Double>", tokens, StringComparison.Ordinal);
        Assert.Contains("Width=\"{DynamicResource SkyIconSize}\"", styles, StringComparison.Ordinal);
    }

    private const double GlyphSize = 16;

    private static System.Collections.Generic.Dictionary<string, string> ReadGlyphDictionary()
    {
        var document = System.Xml.Linq.XDocument.Load(GlyphDictionaryFile);
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

        return document.Descendants()
            .Where(element => element.Name.LocalName == "StreamGeometry")
            .ToDictionary(
                element => (string)element.Attribute(xaml + "Key")!,
                element => element.Value.Trim(),
                StringComparer.Ordinal);
    }

    private static IImage CreateGlyphImage() => new StubImage();

    private sealed class StubImage : IImage
    {
        public Size Size => new(GlyphSize, GlyphSize);

        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect)
        {
        }
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
