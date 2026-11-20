using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.FileMetadata.Tests;

public sealed class FileMetadataPluginTests
{
    private static readonly string[] Extensions = [".jpg"];

    [Fact]
    public async Task JpegXmp_PublishesKeywordsHierarchyCommentAndRating()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(
            keywords: ["sunset", "beach"],
            hierarchical: ["Places|Beach|Sunset"],
            description: "Golden hour over the bay",
            rating: 4));

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal("sunset; beach", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal("Places; Places / Beach; Places / Beach / Sunset",
            Text(values, FileMetadataSchema.HierarchicalKeywords));
        Assert.Equal("Golden hour over the bay", Text(values, FileMetadataSchema.Comment));
        Assert.Equal(4L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task WithoutSplit_HierarchicalPathStaysOneTag()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(hierarchical: ["Places|Beach|Sunset"]));

        var values = (await ExtractAsync(data, Settings(splitHierarchical: false))).Values;

        Assert.Equal("Places|Beach|Sunset", Text(values, FileMetadataSchema.HierarchicalKeywords));
        Assert.DoesNotContain(values, value => value.Key == FileMetadataSchema.Keywords);
    }

    [Fact]
    public async Task PngTextChunks_ProvideKeywordsAndComment()
    {
        var data = MetadataFixtures.Png(text:
        [
            ("Keywords", "cats, dogs"),
            ("Comment", "Two pets on a sofa")
        ]);

        var values = (await ExtractAsync(data, extension: ".png")).Values;

        Assert.Equal("cats; dogs", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal("Two pets on a sofa", Text(values, FileMetadataSchema.Comment));
        Assert.DoesNotContain(values, value => value.Key == FileMetadataSchema.Rating);
    }

    [Fact]
    public async Task PngXmpChunk_IsReadFromInternationalText()
    {
        var data = MetadataFixtures.Png(xmp: MetadataFixtures.Xmp(keywords: ["mountain"], rating: 5));

        var values = (await ExtractAsync(data, extension: ".png")).Values;

        Assert.Equal("mountain", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal(5L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task JpegIptc_ProvidesKeywordsAndCaption()
    {
        var iim = MetadataFixtures.Iim(
            (25, "alpha"), (25, "beta"), (120, "Caption written by the IPTC editor"));
        var data = MetadataFixtures.Jpeg(iim: iim);

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal("alpha; beta", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal("Caption written by the IPTC editor", Text(values, FileMetadataSchema.Comment));
    }

    [Fact]
    public async Task JpegCommentSegment_IsUsedWhenNothingElseProvidesOne()
    {
        var data = MetadataFixtures.Jpeg(comment: "Legacy comment");

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal("Legacy comment", Text(values, FileMetadataSchema.Comment));
        Assert.Single(values);
    }

    [Fact]
    public async Task TiffXpTags_AreReadFromLittleEndianDirectories()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Ascii(0x010E, "Scanned photograph"),
            MetadataFixtures.Utf16(0x9C9E, "family; holiday", littleEndian: true),
            MetadataFixtures.Short(0x4746, 4, littleEndian: true));

        var values = (await ExtractAsync(data, extension: ".tif")).Values;

        Assert.Equal("Scanned photograph", Text(values, FileMetadataSchema.Comment));
        Assert.Equal("family; holiday", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal(4L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task TiffXpTags_AreReadFromBigEndianDirectories()
    {
        // The Windows XP* tags are UCS-2 little-endian even inside a big-endian directory.
        var data = MetadataFixtures.Tiff(littleEndian: false,
            MetadataFixtures.Utf16(0x9C9E, "archive; 1999", littleEndian: true),
            MetadataFixtures.Short(0x4746, 2, littleEndian: false));

        var values = (await ExtractAsync(data, extension: ".tiff")).Values;

        Assert.Equal("archive; 1999", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal(2L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task RatingPercent_IsConvertedToStars()
    {
        var data = MetadataFixtures.Tiff(littleEndian: true,
            MetadataFixtures.Short(0x4749, 50, littleEndian: true));

        var values = (await ExtractAsync(data, extension: ".tif")).Values;

        Assert.Equal(3L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task XmpSidecar_IsReadWithoutAContainer()
    {
        var data = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(keywords: ["sidecar"], rating: 3));

        var values = (await ExtractAsync(data, extension: ".xmp")).Values;

        Assert.Equal("sidecar", Text(values, FileMetadataSchema.Keywords));
        Assert.Equal(3L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task MicrosoftRating_IsConvertedToStars()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(microsoftRating: 80));

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal(4L, Integer(values, FileMetadataSchema.Rating));
    }

    [Fact]
    public async Task RejectedXmpRating_IsOmitted()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(rating: -1));

        var values = (await ExtractAsync(data)).Values;

        Assert.Empty(values);
    }

    [Fact]
    public async Task PdfKeywords_AreSplitIntoSeparateTags()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(pdfKeywords: "invoice, scanned"));

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal("invoice; scanned", Text(values, FileMetadataSchema.Keywords));
    }

    [Theory]
    [InlineData("plain text that is not a container")]
    [InlineData("<?xpacket begin=\"\uFEFF\"?><x:xmpmeta><rdf:RDF><rdf:Description>")]
    public async Task MalformedInput_ReturnsNoValuesWithoutFailing(string payload)
    {
        var result = await ExtractAsync(Encoding.UTF8.GetBytes(payload));

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task EmptySource_ReturnsNoValues()
    {
        var plugin = new FileMetadataPlugin();
        var result = await plugin.ExtractAsync(new MediaItem(".jpg", null, 0),
            new ByteArrayMediaSource([]), null, CancellationToken.None);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task Options_BoundHowMuchOfTheSourceIsScanned()
    {
        var filler = new byte[4096];
        var packet = Encoding.UTF8.GetBytes(MetadataFixtures.Xmp(keywords: ["beyond the prefix"]));
        var data = new byte[filler.Length + packet.Length];
        filler.CopyTo(data, 0);
        packet.CopyTo(data, filler.Length);

        var bounded = await ExtractAsync(data, extension: ".xmp",
            options: new FileMetadataOptions(maxScanBytes: 1024));
        Assert.Empty(bounded.Values);

        var full = await ExtractAsync(data, extension: ".xmp");
        Assert.Equal("beyond the prefix", Text(full.Values, FileMetadataSchema.Keywords));
    }

    [Fact]
    public async Task CollectionDisabled_ReturnsNoValuesAndClaimsNothing()
    {
        var plugin = new FileMetadataPlugin();
        plugin.ApplySettings(Settings(collectionEnabled: false).ToDictionary());

        Assert.Empty(plugin.Descriptor.Extensions);
        var result = await plugin.ExtractAsync(new MediaItem(".jpg", null, 10),
            new ByteArrayMediaSource(MetadataFixtures.Jpeg(MetadataFixtures.Xmp(keywords: ["ignored"]))),
            null, CancellationToken.None);
        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task IndividualOptions_SuppressTheirFieldsOnly()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(
            keywords: ["keep"],
            description: "dropped comment",
            rating: 2));
        var settings = new FileMetadataSettings(
            collectionEnabled: true,
            keywordsEnabled: true,
            splitHierarchicalTags: true,
            commentsEnabled: false,
            ratingsEnabled: false,
            Extensions);

        var values = (await ExtractAsync(data, settings)).Values;

        Assert.Equal("keep", Text(values, FileMetadataSchema.Keywords));
        Assert.DoesNotContain(values, value => value.Key == FileMetadataSchema.Comment);
        Assert.DoesNotContain(values, value => value.Key == FileMetadataSchema.Rating);
    }

    [Fact]
    public async Task KeywordsDisabled_SuppressesBothTagFields()
    {
        var data = MetadataFixtures.Jpeg(MetadataFixtures.Xmp(
            keywords: ["a"],
            hierarchical: ["a|b"]));
        var settings = new FileMetadataSettings(true, keywordsEnabled: false, true, true, true, Extensions);

        var values = (await ExtractAsync(data, settings)).Values;

        Assert.Empty(values);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileMetadataPlugin().ExtractAsync(
            new MediaItem(".jpg", null, 10),
            new ByteArrayMediaSource(MetadataFixtures.Jpeg()),
            null,
            cancellation.Token));
    }

    [Fact]
    public async Task Progress_ReachesCompletion()
    {
        var reports = new RecordingProgress();
        await new FileMetadataPlugin().ExtractAsync(
            new MediaItem(".jpg", null, 10),
            new ByteArrayMediaSource(MetadataFixtures.Jpeg(MetadataFixtures.Xmp(keywords: ["done"]))),
            reports,
            CancellationToken.None);

        Assert.NotEmpty(reports.Fractions);
        Assert.Equal(1, reports.Fractions[^1]);
    }

    [Fact]
    public void Descriptor_DeclaresThePortableFieldRoles()
    {
        var descriptor = new FileMetadataPlugin().Descriptor;

        Assert.Contains(descriptor.Extensions, extension => extension == ".jpg");
        Assert.Contains(descriptor.Extensions, extension => extension == ".xmp");
        Assert.Equal(FileMetadataSchema.SchemaId, descriptor.Schema.Id);
        Assert.Equal(MediaMetadataFieldRole.Tags,
            descriptor.Schema.Fields.Single(field => field.Key == FileMetadataSchema.Keywords).Role);
        Assert.Equal(MediaMetadataFieldRole.Tags,
            descriptor.Schema.Fields.Single(field => field.Key == FileMetadataSchema.HierarchicalKeywords).Role);
        Assert.Equal(MediaMetadataType.Integer,
            descriptor.Schema.Fields.Single(field => field.Key == FileMetadataSchema.Rating).Type);
    }

    private static FileMetadataSettings Settings(bool collectionEnabled = true, bool splitHierarchical = true) =>
        new(collectionEnabled, true, splitHierarchical, true, true, Extensions);

    private static async Task<MediaExtractionResult> ExtractAsync(
        byte[] data,
        FileMetadataSettings? settings = null,
        string extension = ".jpg",
        FileMetadataOptions? options = null)
    {
        var plugin = new FileMetadataPlugin(options ?? FileMetadataOptions.Default);
        if (settings is not null)
        {
            plugin.ApplySettings(settings.ToDictionary());
        }

        return await plugin.ExtractAsync(new MediaItem(extension, null, data.Length),
            new ByteArrayMediaSource(data), null, CancellationToken.None);
    }

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
