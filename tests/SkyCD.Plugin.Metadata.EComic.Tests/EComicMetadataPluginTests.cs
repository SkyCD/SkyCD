using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.EComic.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.EComic.Tests;

/// <summary>
/// Extraction behavior for the metadata capability: which ComicInfo values are published, how user options
/// gate role fields, and how missing, malformed, oversized or unreadable containers degrade.
/// </summary>
public sealed class EComicMetadataPluginTests
{
    [Fact]
    public async Task FullComicInfo_PublishesTypedValuesAndTheDetectedContainer()
    {
        var result = await EComicFixtures.ExtractMetadataAsync(EComicFixtures.ComicWithPage());
        var values = result.Values.ToDictionary(value => value.Key, StringComparer.Ordinal);

        Assert.Equal("zip", values[EComicSchema.Container].Text);
        Assert.Equal("Saga", values[EComicSchema.Series].Text);
        Assert.Equal("Chapter One", values[EComicSchema.Title].Text);
        Assert.Equal("1", values[EComicSchema.Number].Text);
        Assert.Equal("2012", values[EComicSchema.Volume].Text);
        Assert.Equal("Image Comics", values[EComicSchema.Publisher].Text);
        Assert.Equal("Science Fiction", values[EComicSchema.Genre].Text);
        Assert.Equal(2012L, values[EComicSchema.Year].Integer);
        Assert.Equal(22L, values[EComicSchema.PageCount].Integer);
        Assert.Equal("en", values[EComicSchema.LanguageIso].Text);
        Assert.StartsWith("A young family", values[EComicSchema.Summary].Text, StringComparison.Ordinal);

        // Role fields stay unpublished while their option is off, even though they are declared.
        Assert.False(values.ContainsKey(EComicSchema.Writer));
        Assert.False(values.ContainsKey(EComicSchema.PreferredName));
    }

    [Fact]
    public void SchemaDeclaresTheRoleFieldsTheHostApplies()
    {
        Assert.Equal(MediaMetadataFieldRole.Tags,
            EComicSchema.Metadata.Fields.Single(field => field.Key == EComicSchema.Writer).Role);
        Assert.Equal(MediaMetadataFieldRole.PreferredName,
            EComicSchema.Metadata.Fields.Single(field => field.Key == EComicSchema.PreferredName).Role);
    }

    [Fact]
    public async Task Writer_IsPublishedOnlyWhenWriterTagsAreEnabled()
    {
        var withoutTags = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(), settings: EComicFixtures.Settings(tagWriters: false));
        Assert.DoesNotContain(withoutTags.Values, value => value.Key == EComicSchema.Writer);

        var withTags = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(), settings: EComicFixtures.Settings(tagWriters: true));
        Assert.Equal("Brian K. Vaughan, Fiona Staples",
            withTags.Values.Single(value => value.Key == EComicSchema.Writer).Text);
    }

    [Theory]
    [InlineData(EComicNameSource.FileName, null)]
    [InlineData(EComicNameSource.Series, "Saga")]
    [InlineData(EComicNameSource.Title, "Chapter One")]
    public async Task PreferredName_FollowsTheConfiguredNameSource(EComicNameSource source, string? expected)
    {
        var result = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(), settings: EComicFixtures.Settings(nameSource: source));

        var preferred = result.Values.SingleOrDefault(value => value.Key == EComicSchema.PreferredName);
        Assert.Equal(expected, preferred?.Text);
    }

    [Fact]
    public async Task PreferredName_IsOmittedWhenTheSelectedValueIsMissing()
    {
        const string titleOnly = """
            <?xml version="1.0" encoding="utf-8"?>
            <ComicInfo><Title>Only A Title</Title></ComicInfo>
            """;

        var result = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(titleOnly),
            settings: EComicFixtures.Settings(nameSource: EComicNameSource.Series));

        Assert.DoesNotContain(result.Values, value => value.Key == EComicSchema.PreferredName);
        Assert.Equal("Only A Title", result.Values.Single(value => value.Key == EComicSchema.Title).Text);
    }

    [Fact]
    public async Task MissingComicInfo_StillReportsTheContainer()
    {
        var result = await EComicFixtures.ExtractMetadataAsync(EComicFixtures.ComicWithPage(comicInfo: null));

        var value = Assert.Single(result.Values);
        Assert.Equal(EComicSchema.Container, value.Key);
        Assert.Equal("zip", value.Text);
    }

    [Fact]
    public async Task MalformedComicInfo_ReadsAsEmptyInsteadOfFailing()
    {
        var result = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage("<ComicInfo><Series>Saga</Series>"));

        var value = Assert.Single(result.Values);
        Assert.Equal(EComicSchema.Container, value.Key);
    }

    [Fact]
    public async Task OversizedComicInfo_IsIgnored()
    {
        var options = new EComicPluginOptions(maxComicInfoBytes: 16);
        var result = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(), options: options);

        var value = Assert.Single(result.Values);
        Assert.Equal(EComicSchema.Container, value.Key);
    }

    [Fact]
    public async Task LongerThanAllowedValues_AreTruncatedToTheFieldBound()
    {
        var options = new EComicPluginOptions(maxFieldCharacters: 8);
        var result = await EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(), options: options);

        Assert.Equal("Chapter", result.Values.Single(value => value.Key == EComicSchema.Title).Text);
        Assert.Equal("Saga", result.Values.Single(value => value.Key == EComicSchema.Series).Text);
    }

    [Fact]
    public async Task DamagedArchive_YieldsNoValuesInsteadOfAFailure()
    {
        var result = await EComicFixtures.ExtractMetadataAsync("this is definitely not an archive"u8.ToArray());

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task EmptySource_YieldsNoValues()
    {
        var result = await EComicFixtures.ExtractMetadataAsync([]);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task DisabledCollection_PublishesNothingAndClaimsNoExtensions()
    {
        var plugin = new EComicMetadataPlugin();
        plugin.ApplySettings(EComicFixtures.Settings(collectionEnabled: false).ToDictionary());

        Assert.Empty(plugin.Descriptor.Extensions);
        Assert.Empty(plugin.Descriptor.ContentTypes);

        var result = await plugin.ExtractAsync(
            new MediaItem(".cbz", null, 10),
            new ByteArrayMediaSource(EComicFixtures.ComicWithPage()),
            progress: null,
            CancellationToken.None);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void ConfiguredExtensions_AreClaimedExactly()
    {
        var plugin = new EComicMetadataPlugin();
        plugin.ApplySettings(EComicFixtures.Settings(extensions: "CBZ; .cb7 , cbz").ToDictionary());

        Assert.Equal([".cbz", ".cb7"], plugin.Descriptor.Extensions);
    }

    [Fact]
    public async Task PreCancelledExtraction_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EComicFixtures.ExtractMetadataAsync(
            EComicFixtures.ComicWithPage(), cancellationToken: cancelled.Token));
    }
}
