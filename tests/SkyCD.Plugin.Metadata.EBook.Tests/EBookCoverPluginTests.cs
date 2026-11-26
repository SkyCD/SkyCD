using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.EBook.Tests;

/// <summary>
/// Cover generation: configured size, each container format, and the "no cover" states that must stay
/// distinguishable from a failure.
/// </summary>
public sealed class EBookCoverPluginTests
{
    [Fact]
    public async Task EpubCover_IsScaledDownToTheConfiguredSize()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SKColors.Teal));

        var result = await EBookFixtures.GenerateCoverAsync(
            book, EBookFixtures.Settings(extractCovers: true, coverSize: "200"));

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        using var cover = EBookFixtures.Decode(artifact.Data);
        Assert.Equal(200, Math.Max(cover.Width, cover.Height));
        Assert.Equal(133, cover.Width);
        Assert.Equal(200, cover.Height);
    }

    [Fact]
    public async Task SmallCovers_AreNeverUpscaled()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(120, 180, SKColors.Gold));

        var result = await EBookFixtures.GenerateCoverAsync(
            book, EBookFixtures.Settings(extractCovers: true, coverSize: "720"));

        using var cover = EBookFixtures.Decode(Assert.Single(result.Artifacts).Data);
        Assert.Equal(120, cover.Width);
        Assert.Equal(180, cover.Height);
    }

    [Fact]
    public async Task DefaultCoverSize_IsTwoHundredAndFiftyPixels()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(1000, 1500, SKColors.Teal));

        var result = await EBookFixtures.GenerateCoverAsync(book);

        using var cover = EBookFixtures.Decode(Assert.Single(result.Artifacts).Data);
        Assert.Equal(250, Math.Max(cover.Width, cover.Height));
    }

    [Fact]
    public async Task CoverHref_IsResolvedRelativeToThePackageAndUnescaped()
    {
        const string opf = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>Escaped path</dc:title>
                <meta name="cover" content="c"/>
              </metadata>
              <manifest>
                <item id="c" href="images/cover%20art.jpg" media-type="image/jpeg"/>
              </manifest>
            </package>
            """;
        var book = EBookFixtures.Epub(
            opf,
            cover: EBookFixtures.Jpeg(300, 450, SKColors.Teal),
            coverPath: "OEBPS/images/cover art.jpg");

        var result = await EBookFixtures.GenerateCoverAsync(book);

        Assert.Single(result.Artifacts);
    }

    [Fact]
    public async Task GuideOnlyCover_IsUsedWhenTheManifestNamesNoCover()
    {
        const string opf = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="2.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>Guide cover</dc:title>
              </metadata>
              <manifest>
                <item id="c" href="images/front.png" media-type="image/png"/>
              </manifest>
              <guide>
                <reference type="cover" href="images/front.png"/>
              </guide>
            </package>
            """;
        var book = EBookFixtures.Epub(
            opf,
            cover: EBookFixtures.Png(400, 300, SKColors.Orange),
            coverPath: "OEBPS/images/front.png");

        var result = await EBookFixtures.GenerateCoverAsync(book);

        Assert.Single(result.Artifacts);
    }

    [Fact]
    public async Task FictionBookCover_IsDecodedFromItsBinaryElement()
    {
        var book = System.Text.Encoding.UTF8.GetBytes(
            EBookFixtures.Fb2Xml(EBookFixtures.Jpeg(240, 360, SKColors.Navy)));

        var result = await EBookFixtures.GenerateCoverAsync(
            book, EBookFixtures.Settings(extractCovers: true, coverSize: "360"), extension: ".fb2");

        using var cover = EBookFixtures.Decode(Assert.Single(result.Artifacts).Data);
        Assert.Equal(360, Math.Max(cover.Width, cover.Height));
    }

    [Fact]
    public async Task MobipocketCoverRecord_IsExtracted()
    {
        var book = EBookFixtures.Mobi(cover: EBookFixtures.Jpeg(300, 450, SKColors.Teal));

        var result = await EBookFixtures.GenerateCoverAsync(book, extension: ".mobi");

        Assert.Single(result.Artifacts);
    }

    [Fact]
    public async Task FakeCoverFlag_SuppressesThePlaceholderImage()
    {
        var book = EBookFixtures.Mobi(
            cover: EBookFixtures.Jpeg(300, 450, SKColors.Teal),
            fakeCover: true);

        var result = await EBookFixtures.GenerateCoverAsync(book, extension: ".mobi");

        Assert.Empty(result.Artifacts);
    }

    [Theory]
    [InlineData("epub")]
    [InlineData("fb2")]
    [InlineData("mobi")]
    public async Task BooksWithoutACover_YieldAnEmptyArtifactList(string kind)
    {
        var (book, extension) = kind switch
        {
            "fb2" => (System.Text.Encoding.UTF8.GetBytes(EBookFixtures.Fb2Xml()), ".fb2"),
            "mobi" => (EBookFixtures.Mobi(), ".mobi"),
            _ => (EBookFixtures.Epub(), ".epub")
        };

        var result = await EBookFixtures.GenerateCoverAsync(book, extension: extension);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task DisabledOptions_YieldAnEmptyArtifactList()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SKColors.Teal));

        Assert.Empty((await EBookFixtures.GenerateCoverAsync(
            book, EBookFixtures.Settings(extractCovers: false))).Artifacts);
        Assert.Empty((await EBookFixtures.GenerateCoverAsync(
            book, EBookFixtures.Settings(collectionEnabled: false, extractCovers: true))).Artifacts);
    }

    [Fact]
    public async Task NonImageCoverPayload_IsIgnored()
    {
        var book = EBookFixtures.Epub(cover: System.Text.Encoding.ASCII.GetBytes("this is not an image"));

        var result = await EBookFixtures.GenerateCoverAsync(book);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task UndecodableCover_DoesNotFailThePreviewPass()
    {
        var fakeJpeg = new byte[512];
        fakeJpeg[0] = 0xFF;
        fakeJpeg[1] = 0xD8;
        fakeJpeg[2] = 0xFF;
        var book = EBookFixtures.Epub(cover: fakeJpeg);

        var result = await EBookFixtures.GenerateCoverAsync(book);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var book = EBookFixtures.Epub(cover: EBookFixtures.Jpeg(600, 900, SKColors.Teal));
        var source = new ByteArrayMediaSource(book);
        var plugin = new EBookCoverPlugin();
        plugin.ApplySettings(EBookFixtures.Settings(extractCovers: true).ToDictionary());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.GeneratePreviewAsync(
            new MediaItem(".epub", null, book.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public void Descriptor_RequiresBothOptionsForIndexTimePreviews()
    {
        var plugin = new EBookCoverPlugin();
        Assert.Empty(plugin.Descriptor.Extensions);

        plugin.ApplySettings(EBookFixtures.Settings(extractCovers: true).ToDictionary());
        Assert.NotEmpty(plugin.Descriptor.Extensions);
        Assert.True(plugin.Descriptor.IndexTimePreview);

        plugin.ApplySettings(EBookFixtures.Settings(extractCovers: false).ToDictionary());
        Assert.Empty(plugin.Descriptor.Extensions);
    }
}
