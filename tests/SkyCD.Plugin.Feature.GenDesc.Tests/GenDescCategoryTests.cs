using System.Collections.Generic;
using SkyCD.Plugin.Feature.GenDesc.Models;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class GenDescCategoryTests
{
    private static readonly Dictionary<string, string> NoFacts = new();

    [Theory]
    [InlineData(".mkv", "movie")]
    [InlineData(".MP4", "movie")]
    [InlineData(".ts", "tv")]
    [InlineData(".flac", "music")]
    [InlineData(".m4b", "audiobook")]
    [InlineData(".epub", "ebook")]
    [InlineData(".cbz", "comic")]
    [InlineData(".exe", "software")]
    [InlineData(".iso", "game")]
    [InlineData(".gba", "rom")]
    [InlineData(".png", "image")]
    [InlineData(".woff2", "font")]
    [InlineData(".xyz", "other")]
    [InlineData("", "other")]
    public void ExtensionDrivesTheCategory(string extension, string expected)
    {
        Assert.Equal(expected, GenDescCategory.Detect(extension, "application/octet-stream", NoFacts).Key);
    }

    [Fact]
    public void MetadataFactsRefineAmbiguousFiles()
    {
        var episode = new Dictionary<string, string> { ["Season"] = "2", ["Episode"] = "4" };
        Assert.Equal("tv", GenDescCategory.Detect(".mkv", "video/x-matroska", episode).Key);

        var audiobook = new Dictionary<string, string> { ["Narrator"] = "Someone" };
        Assert.Equal("audiobook", GenDescCategory.Detect(".mp3", "audio/mpeg", audiobook).Key);

        var course = new Dictionary<string, string> { ["Instructor"] = "Teacher" };
        Assert.Equal("course", GenDescCategory.Detect(".mp4", "video/mp4", course).Key);
    }

    [Fact]
    public void MimeTypeIsUsedWhenTheExtensionIsUnknown()
    {
        Assert.Equal("movie", GenDescCategory.Detect(".dat", "video/mp4", NoFacts).Key);
        Assert.Equal("music", GenDescCategory.Detect("", "audio/flac", NoFacts).Key);
        Assert.Equal("image", GenDescCategory.Detect(".dat", "image/png", NoFacts).Key);
        Assert.Equal("font", GenDescCategory.Detect(".dat", "application/font-woff", NoFacts).Key);
        Assert.Equal("comic", GenDescCategory.Detect(".dat", "application/vnd.comicbook+zip", NoFacts).Key);
        Assert.Equal("ebook", GenDescCategory.Detect(".dat", "application/epub+zip", NoFacts).Key);
        Assert.Equal("other", GenDescCategory.Detect("", "", NoFacts).Key);
    }

    [Theory]
    [InlineData(".mkv", "video/x-matroska")]
    [InlineData(".mp4", "video/mp4")]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".opus", "audio/ogg")]
    [InlineData(".cbz", "application/vnd.comicbook+zip")]
    [InlineData("mkv", "video/x-matroska")]
    [InlineData(".unknown", "application/octet-stream")]
    [InlineData(null, "application/octet-stream")]
    public void MimeFromExtensionCoversTheCliTypes(string? extension, string expected)
    {
        Assert.Equal(expected, GenDescCategory.MimeFromExtension(extension));
    }

    [Theory]
    [InlineData("movie", "Movie")]
    [InlineData("audiobook", "Audiobook")]
    [InlineData("rom", "ROM")]
    [InlineData("anything", "Other")]
    public void TitleForResolvesDisplayNames(string key, string expected)
    {
        Assert.Equal(expected, GenDescCategory.TitleFor(key));
    }
}
