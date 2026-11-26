using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Image.Tests;

/// <summary>
/// Covers the portable tags stored in TIFF containers and the TIFF-based RAW formats. Those pixels are not
/// decoded for previews, so this capability publishes tags only, from a bounded header prefix. Standalone XMP
/// sidecars belong to the separate XMP plugin.
/// </summary>
public sealed class ContainerTagsPluginTests
{
    [Fact]
    public async Task TiffXpTags_AreReadFromLittleEndianDirectories()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Ascii(0x010E, "Scanned photograph"),
            MetadataFixtures.Utf16(0x9C9E, "family; holiday", littleEndian: true),
            MetadataFixtures.Short(0x4746, 4, littleEndian: true));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal("Scanned photograph", Text(values, ImageSchema.Comment));
        Assert.Equal("family; holiday", Text(values, ImageSchema.Tags));
        Assert.Equal(4L, Integer(values, ImageSchema.Rating));
    }

    [Fact]
    public async Task TiffXpTags_AreReadFromBigEndianDirectories()
    {
        // The Windows XP* tags are UCS-2 little-endian even inside a big-endian directory.
        var data = MetadataFixtures.Tiff(littleEndian: false,
            MetadataFixtures.Utf16(0x9C9E, "archive; 1999", littleEndian: true),
            MetadataFixtures.Short(0x4746, 2, littleEndian: false));

        var values = (await ExtractAsync(data, ".tiff")).Values;

        Assert.Equal("archive; 1999", Text(values, ImageSchema.Tags));
        Assert.Equal(2L, Integer(values, ImageSchema.Rating));
    }

    [Fact]
    public async Task RatingPercent_UsesTheShellStarRanges()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Short(0x4749, 50, littleEndian: true));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal(3L, Integer(values, ImageSchema.Rating));
    }

    [Fact]
    public async Task ExplorerRatingOf25Percent_BecomesTwoStars()
    {
        // Explorer writes 25 for two stars; dividing by 20 would wrongly report one.
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Short(0x4746, 25, littleEndian: true));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal(2L, Integer(values, ImageSchema.Rating));
    }

    [Fact]
    public async Task TiffXmpTag_ProvidesEveryPortableField()
    {
        var packet = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(
            keywords: ["raw", "edited"],
            description: "Developed in-camera",
            rating: 5));
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Raw(0x02BC, packet));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal("raw; edited", Text(values, ImageSchema.Tags));
        Assert.Equal("Developed in-camera", Text(values, ImageSchema.Comment));
        Assert.Equal(5L, Integer(values, ImageSchema.Rating));
    }

    [Fact]
    public async Task TiffIptcTag_ProvidesKeywordsAndCaption()
    {
        var iim = MetadataFixtures.Iim(
            (25, "alpha"), (25, "beta"), (120, "Caption written by the IPTC editor"));
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Raw(0x83BB, iim));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal("alpha; beta", Text(values, ImageSchema.Tags));
        Assert.Equal("Caption written by the IPTC editor", Text(values, ImageSchema.Comment));
    }

    [Fact]
    public async Task MicrosoftRating_IsConvertedToStars()
    {
        var packet = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(microsoftRating: 80));
        var data = MetadataFixtures.Tiff(littleEndian: true, MetadataFixtures.Raw(0x02BC, packet));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal(4L, Integer(values, ImageSchema.Rating));
    }

    [Fact]
    public async Task ContainerIsRecognizedByMagicBytes_NotTheExtension()
    {
        // The extension does not claim this payload; without a TIFF header nothing is parsed.
        var data = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(keywords: ["not a container"]));

        var result = await ExtractAsync(data, ".tif");

        Assert.Empty(result.Values);
    }

    [Theory]
    [InlineData("plain text that is not a container")]
    [InlineData("<?xpacket begin=\"\uFEFF\"?><x:xmpmeta><rdf:RDF><rdf:Description>")]
    [InlineData("II*\u0000\u0008\u0000\u0000\u0000\u0001\u0000")]
    public async Task MalformedInput_ReturnsNoValuesWithoutFailing(string payload)
    {
        var result = await ExtractAsync(Encoding.UTF8.GetBytes(payload), ".tif");

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task TiffWithAbsurdEntryCount_IsIgnoredInsteadOfThrowing()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true);
        data[8] = 0xFF;
        data[9] = 0xFF;

        var result = await ExtractAsync(data, ".tif");

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task EmptySource_ReturnsNoValues()
    {
        var result = await new ContainerTagsPlugin().ExtractAsync(new MediaItem(".tif", null, 0),
            new ByteArrayMediaSource([]), null, CancellationToken.None);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task OnlyTheBoundedPrefixIsScanned()
    {
        var filler = new byte[1024 * 1024];
        var tiff = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9E, "beyond the prefix", littleEndian: true));
        var data = new byte[filler.Length + tiff.Length];
        filler.CopyTo(data, 0);
        tiff.CopyTo(data, filler.Length);

        var result = await ExtractAsync(data, ".tif");

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ContainerTagsPlugin().ExtractAsync(
            new MediaItem(".tif", null, 10),
            new ByteArrayMediaSource(MetadataFixtures.Tiff(littleEndian: true)),
            null,
            cancellation.Token));
    }

    [Fact]
    public async Task Progress_ReachesCompletion()
    {
        var reports = new RecordingProgress();
        await new ContainerTagsPlugin().ExtractAsync(
            new MediaItem(".tif", null, 10),
            new ByteArrayMediaSource(MetadataFixtures.Tiff(littleEndian: true)),
            reports,
            CancellationToken.None);

        Assert.NotEmpty(reports.Fractions);
        Assert.Equal(1, reports.Fractions[^1]);
    }

    [Fact]
    public void Descriptor_ClaimsOnlyTiffExtensions()
    {
        var descriptor = new ContainerTagsPlugin().Descriptor;

        Assert.Equal([".tif", ".tiff"], descriptor.Extensions);
        Assert.Equal(["image/tiff"], descriptor.ContentTypes);
        Assert.DoesNotContain(".jpg", descriptor.Extensions);
        Assert.DoesNotContain(".png", descriptor.Extensions);
        Assert.DoesNotContain(".xmp", descriptor.Extensions);
        Assert.Equal(ImageSchema.ContainerTagsSchemaId, descriptor.Schema.Id);
        Assert.Equal(MediaMetadataFieldRole.Tags,
            descriptor.Schema.Fields.Single(field => field.Key == ImageSchema.Tags).Role);
        Assert.Equal(MediaMetadataType.Integer,
            descriptor.Schema.Fields.Single(field => field.Key == ImageSchema.Rating).Type);
    }

    private static async Task<MediaExtractionResult> ExtractAsync(byte[] data, string extension) =>
        await new ContainerTagsPlugin().ExtractAsync(new MediaItem(extension, null, data.Length),
            new ByteArrayMediaSource(data), null, CancellationToken.None);

    private static string? Text(IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.SingleOrDefault(value => value.Key == key)?.Text;

    private static long? Integer(IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.SingleOrDefault(value => value.Key == key)?.Integer;

    /// <summary>Synchronous progress recorder; <see cref="Progress{T}"/> posts asynchronously without a UI context.</summary>
    private sealed class RecordingProgress : IProgress<MediaProgress>
    {
        public List<double> Fractions { get; } = [];

        public void Report(MediaProgress value) => Fractions.Add(value.Fraction);
    }
}
