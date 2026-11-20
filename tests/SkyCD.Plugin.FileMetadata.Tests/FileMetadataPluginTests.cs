using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

/// <summary>
/// Covers the portable tags that travel inside TIFF containers, TIFF-based RAW files and standalone XMP
/// sidecars. The image formats the image plugin presents are not claimed here.
/// </summary>
public sealed class FileMetadataPluginTests
{
    [Fact]
    public async Task TiffXpTags_AreReadFromLittleEndianDirectories()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Ascii(0x010E, "Scanned photograph"),
            MetadataFixtures.Utf16(0x9C9E, "family; holiday", littleEndian: true),
            MetadataFixtures.Short(0x4746, 4, littleEndian: true));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal("Scanned photograph", Text(values, FileMetadataSchema.Comment));
        Assert.Equal("family; holiday", Text(values, FileMetadataSchema.Tags));
        Assert.Equal(4L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task TiffXpTags_AreReadFromBigEndianDirectories()
    {
        // The Windows XP* tags are UCS-2 little-endian even inside a big-endian directory.
        var data = MetadataFixtures.Tiff(littleEndian: false,
            MetadataFixtures.Utf16(0x9C9E, "archive; 1999", littleEndian: true),
            MetadataFixtures.Short(0x4746, 2, littleEndian: false));

        var values = (await ExtractAsync(data, ".tiff")).Values;

        Assert.Equal("archive; 1999", Text(values, FileMetadataSchema.Tags));
        Assert.Equal(2L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task RatingPercent_UsesTheShellStarRanges()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Short(0x4749, 50, littleEndian: true));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal(3L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task ExplorerRatingOf25Percent_BecomesTwoStars()
    {
        // Explorer writes 25 for two stars; dividing by 20 would wrongly report one.
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Short(0x4746, 25, littleEndian: true));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal(2L, Integer(values, FileMetadataSchema.Rating));
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

        var values = (await ExtractAsync(data, ".dng")).Values;

        Assert.Equal("raw; edited", Text(values, FileMetadataSchema.Tags));
        Assert.Equal("Developed in-camera", Text(values, FileMetadataSchema.Comment));
        Assert.Equal(5L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task TiffIptcTag_ProvidesKeywordsAndCaption()
    {
        var iim = MetadataFixtures.Iim(
            (25, "alpha"), (25, "beta"), (120, "Caption written by the IPTC editor"));
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Raw(0x83BB, iim));

        var values = (await ExtractAsync(data, ".tif")).Values;

        Assert.Equal("alpha; beta", Text(values, FileMetadataSchema.Tags));
        Assert.Equal("Caption written by the IPTC editor", Text(values, FileMetadataSchema.Comment));
    }

    [Fact]
    public async Task XmpSidecar_IsReadWithoutAContainer()
    {
        var data = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(keywords: ["sidecar"], rating: 3));

        var values = (await ExtractAsync(data, ".xmp")).Values;

        Assert.Equal("sidecar", Text(values, FileMetadataSchema.Tags));
        Assert.Equal(3L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task MicrosoftRating_IsConvertedToStars()
    {
        var packet = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(microsoftRating: 80));
        var data = MetadataFixtures.Tiff(littleEndian: true, MetadataFixtures.Raw(0x02BC, packet));

        var values = (await ExtractAsync(data, ".arw")).Values;

        Assert.Equal(4L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task RejectedXmpRating_IsOmitted()
    {
        var data = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(rating: -1));

        var values = (await ExtractAsync(data, ".xmp")).Values;

        Assert.Empty(values);
    }

    [Fact]
    public async Task PdfKeywords_AreSplitIntoSeparateTags()
    {
        var data = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(pdfKeywords: "invoice, scanned"));

        var values = (await ExtractAsync(data, ".xmp")).Values;

        Assert.Equal("invoice; scanned", Text(values, FileMetadataSchema.Tags));
    }

    [Fact]
    public async Task ContainerIsRecognizedByMagicBytes_NotTheExtension()
    {
        // A TIFF payload saved under a sidecar name is still read as a container.
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Utf16(0x9C9E, "misnamed", littleEndian: true));

        var values = (await ExtractAsync(data, ".xmp")).Values;

        Assert.Equal("misnamed", Text(values, FileMetadataSchema.Tags));
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
        var plugin = new FileMetadataPlugin();
        var result = await plugin.ExtractAsync(new MediaItem(".tif", null, 0),
            new ByteArrayMediaSource([]), null, CancellationToken.None);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task OnlyTheBoundedPrefixIsScanned()
    {
        var filler = new byte[1024 * 1024];
        var packet = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(keywords: ["beyond the prefix"]));
        var data = new byte[filler.Length + packet.Length];
        filler.CopyTo(data, 0);
        packet.CopyTo(data, filler.Length);

        var result = await ExtractAsync(data, ".xmp");

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileMetadataPlugin().ExtractAsync(
            new MediaItem(".tif", null, 10),
            new ByteArrayMediaSource(MetadataFixtures.Tiff(littleEndian: true)),
            null,
            cancellation.Token));
    }

    [Fact]
    public async Task Progress_ReachesCompletion()
    {
        var reports = new RecordingProgress();
        await new FileMetadataPlugin().ExtractAsync(
            new MediaItem(".xmp", null, 10),
            new ByteArrayMediaSource(Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(keywords: ["done"]))),
            reports,
            CancellationToken.None);

        Assert.NotEmpty(reports.Fractions);
        Assert.Equal(1, reports.Fractions[^1]);
    }

    [Fact]
    public void Descriptor_ClaimsOnlyTheCarriersTheImagePluginDoesNotPresent()
    {
        var descriptor = new FileMetadataPlugin().Descriptor;

        Assert.Equal(
            [".tif", ".tiff", ".dng", ".cr2", ".nef", ".arw", ".xmp"],
            descriptor.Extensions);
        Assert.DoesNotContain(".jpg", descriptor.Extensions);
        Assert.DoesNotContain(".png", descriptor.Extensions);
        Assert.Equal(FileMetadataSchema.EmbeddedSchemaId, descriptor.Schema.Id);
        Assert.Equal(MediaMetadataFieldRole.Tags,
            descriptor.Schema.Fields.Single(field => field.Key == FileMetadataSchema.Tags).Role);
        Assert.Equal(MediaMetadataType.Integer,
            descriptor.Schema.Fields.Single(field => field.Key == FileMetadataSchema.Rating).Type);
    }

    private static async Task<MediaExtractionResult> ExtractAsync(byte[] data, string extension) =>
        await new FileMetadataPlugin().ExtractAsync(new MediaItem(extension, null, data.Length),
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
