using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using Xunit;

namespace SkyCD.Plugin.EComic.Tests;

/// <summary>
/// Cover generation: configured size, page order, and the "no cover" states that must stay distinguishable
/// from a failure.
/// </summary>
public sealed class EComicCoverPluginTests
{
    [Fact]
    public async Task FirstPage_IsScaledDownToTheConfiguredSize()
    {
        var archive = EComicFixtures.ComicWithPage(pageWidth: 600, pageHeight: 900);
        var result = await EComicFixtures.GenerateCoverAsync(
            archive, EComicFixtures.Settings(extractCovers: true, coverSize: "200"));

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        using var cover = EComicFixtures.Decode(artifact.Data);
        Assert.Equal(200, Math.Max(cover.Width, cover.Height));
        Assert.Equal(133, cover.Width);
        Assert.Equal(200, cover.Height);
    }

    [Fact]
    public async Task SmallPages_AreNeverUpscaled()
    {
        var archive = EComicFixtures.ComicWithPage(pageWidth: 120, pageHeight: 180);
        var result = await EComicFixtures.GenerateCoverAsync(
            archive, EComicFixtures.Settings(extractCovers: true, coverSize: "720"));

        using var cover = EComicFixtures.Decode(Assert.Single(result.Artifacts).Data);
        Assert.Equal(120, cover.Width);
        Assert.Equal(180, cover.Height);
    }

    [Fact]
    public async Task PagesAreChosenInNaturalOrder()
    {
        var archive = EComicFixtures.Cbz(
            ("ComicInfo.xml", EComicFixtures.ComicInfoBytes()),
            ("page10.jpg", EComicFixtures.Jpeg(100, 100, SKColors.Red)),
            ("page2.jpg", EComicFixtures.Jpeg(60, 80, SKColors.Blue)));

        var result = await EComicFixtures.GenerateCoverAsync(
            archive, EComicFixtures.Settings(extractCovers: true));

        using var cover = EComicFixtures.Decode(Assert.Single(result.Artifacts).Data);
        Assert.Equal(60, cover.Width);
        Assert.Equal(80, cover.Height);
    }

    [Fact]
    public async Task DisabledCovers_ProduceNoArtifactAndClaimNoExtensions()
    {
        var plugin = new EComicCoverPlugin();
        plugin.ApplySettings(EComicFixtures.Settings(collectionEnabled: true, extractCovers: false).ToDictionary());

        Assert.Empty(plugin.Descriptor.Extensions);
        var result = await EComicFixtures.GenerateCoverAsync(
            EComicFixtures.ComicWithPage(), EComicFixtures.Settings(extractCovers: false));
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task DisabledCollection_ProducesNoArtifact()
    {
        var result = await EComicFixtures.GenerateCoverAsync(
            EComicFixtures.ComicWithPage(),
            EComicFixtures.Settings(collectionEnabled: false, extractCovers: true));

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task ArchiveWithoutPages_ProducesNoArtifact()
    {
        var archive = EComicFixtures.Cbz(("ComicInfo.xml", EComicFixtures.ComicInfoBytes()));
        var result = await EComicFixtures.GenerateCoverAsync(
            archive, EComicFixtures.Settings(extractCovers: true));

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task PageAboveThePixelBound_ProducesNoArtifact()
    {
        var archive = EComicFixtures.ComicWithPage(pageWidth: 20, pageHeight: 20);
        var result = await EComicFixtures.GenerateCoverAsync(
            archive,
            EComicFixtures.Settings(extractCovers: true),
            new EComicPluginOptions(maxPagePixels: 100));

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task PageAboveTheByteBound_ProducesNoArtifact()
    {
        var archive = EComicFixtures.ComicWithPage();
        var result = await EComicFixtures.GenerateCoverAsync(
            archive,
            EComicFixtures.Settings(extractCovers: true),
            new EComicPluginOptions(maxPageBytes: 16));

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task DamagedArchive_ProducesNoArtifact()
    {
        var result = await EComicFixtures.GenerateCoverAsync("not an archive"u8.ToArray());

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task PreCancelledCoverGeneration_Throws()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EComicFixtures.GenerateCoverAsync(
            EComicFixtures.ComicWithPage(),
            EComicFixtures.Settings(extractCovers: true),
            cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Descriptor_MarksTheCoverAsAnIndexTimePreview()
    {
        var plugin = new EComicCoverPlugin();
        plugin.ApplySettings(EComicFixtures.Settings(extractCovers: true).ToDictionary());

        Assert.False(plugin.Descriptor.IsOptIn);
        Assert.True(plugin.Descriptor.IndexTimePreview);
        Assert.Equal(EComicSchema.PreviewSchemaId, plugin.Descriptor.Schema.Id);
        Assert.NotEmpty(plugin.Descriptor.Extensions);
        Assert.Contains(".cbz", plugin.Descriptor.Extensions);
        Assert.Contains(".cbr", plugin.Descriptor.Extensions);
        Assert.Contains(".cb7", plugin.Descriptor.Extensions);
    }

    [Fact]
    public void ScaleTo_PreservesAspectAndNeverDropsAnEdge()
    {
        Assert.Equal((200, 133), EComicCoverRenderer.ScaleTo(600, 400, 200));
        Assert.Equal((133, 200), EComicCoverRenderer.ScaleTo(400, 600, 200));
        Assert.Equal((1, 1), EComicCoverRenderer.ScaleTo(10_000, 10, 1));
        Assert.Equal((120, 180), EComicCoverRenderer.ScaleTo(120, 180, 720));
    }
}
