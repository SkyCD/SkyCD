using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

public sealed class FileMetadataSettingsTests
{
    [Fact]
    public void Defaults_EnableEverythingPortable()
    {
        var settings = FileMetadataSettings.Default;

        Assert.True(settings.CollectionEnabled);
        Assert.True(settings.KeywordsEnabled);
        Assert.True(settings.SplitHierarchicalTags);
        Assert.True(settings.CommentsEnabled);
        Assert.True(settings.RatingsEnabled);
        Assert.Contains(".jpg", settings.Extensions);
        Assert.Contains(".xmp", settings.Extensions);
    }

    [Fact]
    public void RoundTrip_PreservesEveryOption()
    {
        var original = new FileMetadataSettings(true, false, false, true, false, [".jpg", ".png"]);

        var restored = FileMetadataSettings.From(original.ToDictionary());

        Assert.False(restored.KeywordsEnabled);
        Assert.False(restored.SplitHierarchicalTags);
        Assert.False(restored.RatingsEnabled);
        Assert.Equal([".jpg", ".png"], restored.Extensions);
    }

    [Fact]
    public void InvalidValues_FallBackToDefaults()
    {
        var settings = FileMetadataSettings.From(new Dictionary<string, string?>
        {
            [FileMetadataSettings.CollectionEnabledKey] = "not a boolean",
            [FileMetadataSettings.SplitHierarchicalTagsKey] = null
        });

        Assert.Equal(FileMetadataSettings.Default.CollectionEnabled, settings.CollectionEnabled);
        Assert.Equal(FileMetadataSettings.Default.SplitHierarchicalTags, settings.SplitHierarchicalTags);
    }

    [Theory]
    [InlineData("", ".jpg")]
    [InlineData("jpg;PNG ; jpg", ".jpg")]
    [InlineData("*.jpg/../x", ".jpg")]
    public void ParseExtensions_NormalizesAndFallsBack(string configured, string expected)
    {
        var extensions = FileMetadataSettings.ParseExtensions(configured);

        Assert.Contains(expected, extensions);
        Assert.All(extensions, extension => Assert.StartsWith(".", extension));
    }

    [Fact]
    public void ParseExtensions_CapsTheList()
    {
        var configured = string.Join(';', Enumerable.Range(0, 100).Select(index => ".e" + index));

        Assert.Equal(FileMetadataSettings.MaxExtensionCount,
            FileMetadataSettings.ParseExtensions(configured).Count);
    }
}
