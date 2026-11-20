using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Fonts.Tests;

public sealed class FontMetadataPluginTests
{
    [Fact]
    public async Task ExtractAsync_PublishesFamilyStyleVersionAndCopyright()
    {
        var font = FontFixtures.Sfnt("Source Serif Pro", "SemiBold", version: "Version 1.005",
            copyright: "Copyright 2020 Adobe", postScriptName: "SourceSerifPro-SemiBold");

        var result = await FontFixtures.ExtractAsync(font);

        Assert.Equal("Source Serif Pro", Value(result, FontSchema.Family).Text);
        Assert.Equal("SemiBold", Value(result, FontSchema.Subfamily).Text);
        Assert.Equal("Source Serif Pro SemiBold", Value(result, FontSchema.FullName).Text);
        Assert.Equal("SourceSerifPro-SemiBold", Value(result, FontSchema.PostScriptName).Text);
        Assert.Equal("Version 1.005", Value(result, FontSchema.Version).Text);
        Assert.Equal("Copyright 2020 Adobe", Value(result, FontSchema.Copyright).Text);
        Assert.DoesNotContain(result.Values, value => value.Key == FontSchema.FaceCount);
    }

    [Fact]
    public async Task ExtractAsync_OmitsFieldsTheFontDoesNotCarry()
    {
        var font = FontFixtures.Sfnt("Hummingbird", subfamily: null, version: null, copyright: null);

        var result = await FontFixtures.ExtractAsync(font);

        Assert.Equal("Hummingbird", Value(result, FontSchema.Family).Text);
        Assert.DoesNotContain(result.Values, value => value.Key == FontSchema.Subfamily);
        Assert.DoesNotContain(result.Values, value => value.Key == FontSchema.Version);
        Assert.DoesNotContain(result.Values, value => value.Key == FontSchema.Copyright);
    }

    [Fact]
    public async Task ExtractAsync_ReportsTheFaceCountOfACollection()
    {
        var collection = FontFixtures.Collection(
            FontFixtures.Sfnt("First Face", "Bold"),
            FontFixtures.Sfnt("Second Face", "Italic"),
            FontFixtures.Sfnt("Third Face", "Book"));

        var result = await FontFixtures.ExtractAsync(collection, extension: ".ttc");

        Assert.Equal("First Face", Value(result, FontSchema.Family).Text);
        Assert.Equal(3, Value(result, FontSchema.FaceCount).Integer);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsNoValuesForADamagedFont()
    {
        var damaged = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x09, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00 };

        var result = await FontFixtures.ExtractAsync(damaged);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsNoValuesForBytesThatAreNotAFont()
    {
        var result = await FontFixtures.ExtractAsync("definitely not a font"u8.ToArray());

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task DisabledCollection_ClaimsNoExtensionsAndPublishesNothing()
    {
        var plugin = new FontMetadataPlugin();
        plugin.ApplySettings(FontFixtures.Settings(collectionEnabled: false));

        Assert.Empty(plugin.Descriptor.Extensions);
        var result = await plugin.ExtractAsync(
            new MediaItem(".ttf", null, 100),
            new ByteArrayMediaSource(FontFixtures.Sfnt("Ignored")),
            progress: null,
            default);

        Assert.Empty(result.Values);
    }

    [Fact]
    public void ConfiguredExtensions_AreClaimedByTheDescriptor()
    {
        var plugin = new FontMetadataPlugin();
        plugin.ApplySettings(FontFixtures.Settings(extensions: "ttf;woff2"));

        Assert.Equal([".ttf", ".woff2"], plugin.Descriptor.Extensions);
    }

    [Fact]
    public async Task ExtractAsync_AcceptsAnOpenTypeFlavor()
    {
        var font = FontFixtures.Sfnt("CFF Face", sfntVersion: 0x4F54544F);

        var result = await FontFixtures.ExtractAsync(font, extension: ".otf");

        Assert.Equal("CFF Face", Value(result, FontSchema.Family).Text);
    }

    private static MediaMetadataValue Value(MediaExtractionResult result, string key) =>
        Assert.Single(result.Values, value => value.Key == key);
}
