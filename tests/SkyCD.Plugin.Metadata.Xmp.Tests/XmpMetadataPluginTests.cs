using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Xmp.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.Xmp.Tests;

/// <summary>
/// Covers the keywords, description and rating stored in a standalone XMP sidecar. Packets embedded inside
/// images or TIFF files are read by the plugins that claim those containers.
/// </summary>
public sealed class XmpMetadataPluginTests
{
    [Fact]
    public async Task Sidecar_ProvidesKeywordsDescriptionAndRating()
    {
        var data = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(
            keywords: ["sidecar", "travel"],
            description: "Copenhagen harbour",
            rating: 3));

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal("sidecar; travel", Text(values, XmpSchema.Tags));
        Assert.Equal("Copenhagen harbour", Text(values, XmpSchema.Comment));
        Assert.Equal(3L, Integer(values, XmpSchema.Rating));
    }

    [Fact]
    public async Task RejectedRating_IsOmitted()
    {
        var data = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(rating: -1));

        var result = await ExtractAsync(data);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task PdfKeywords_AreSplitIntoSeparateTags()
    {
        var data = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(pdfKeywords: "invoice, scanned"));

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal("invoice; scanned", Text(values, XmpSchema.Tags));
    }

    [Fact]
    public async Task MicrosoftRating_IsConvertedToStars()
    {
        var data = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(microsoftRating: 80));

        var values = (await ExtractAsync(data)).Values;

        Assert.Equal(4L, Integer(values, XmpSchema.Rating));
    }

    [Theory]
    [InlineData("plain text that is not a packet")]
    [InlineData("<?xpacket begin=\"\uFEFF\"?><x:xmpmeta><rdf:RDF><rdf:Description>")]
    [InlineData("<rdf:RDF><broken>")]
    public async Task MalformedInput_ReturnsNoValuesWithoutFailing(string payload)
    {
        var result = await ExtractAsync(Encoding.UTF8.GetBytes(payload));

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task EmptySource_ReturnsNoValues()
    {
        var result = await new XmpMetadataPlugin().ExtractAsync(new MediaItem(".xmp", null, 0),
            new ByteArrayMediaSource([]), null, CancellationToken.None);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task OnlyTheBoundedPrefixIsScanned()
    {
        var filler = new byte[1024 * 1024];
        var packet = Encoding.UTF8.GetBytes(XmpFixtures.Xmp(keywords: ["beyond the prefix"]));
        var data = new byte[filler.Length + packet.Length];
        filler.CopyTo(data, 0);
        packet.CopyTo(data, filler.Length);

        var result = await ExtractAsync(data);

        Assert.Empty(result.Values);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new XmpMetadataPlugin().ExtractAsync(
            new MediaItem(".xmp", null, 10),
            new ByteArrayMediaSource(Encoding.UTF8.GetBytes(XmpFixtures.Xmp(keywords: ["done"]))),
            null,
            cancellation.Token));
    }

    [Fact]
    public async Task Progress_ReachesCompletion()
    {
        var reports = new RecordingProgress();
        await new XmpMetadataPlugin().ExtractAsync(
            new MediaItem(".xmp", null, 10),
            new ByteArrayMediaSource(Encoding.UTF8.GetBytes(XmpFixtures.Xmp(keywords: ["done"]))),
            reports,
            CancellationToken.None);

        Assert.NotEmpty(reports.Fractions);
        Assert.Equal(1, reports.Fractions[^1]);
    }

    [Fact]
    public void Descriptor_ClaimsOnlySidecars()
    {
        var descriptor = new XmpMetadataPlugin().Descriptor;

        Assert.Equal([".xmp"], descriptor.Extensions);
        Assert.Equal(["application/rdf+xml"], descriptor.ContentTypes);
        Assert.Equal(XmpSchema.SchemaId, descriptor.Schema.Id);
        Assert.Equal(MediaMetadataFieldRole.Tags,
            descriptor.Schema.Fields.Single(field => field.Key == XmpSchema.Tags).Role);
        Assert.Equal(MediaMetadataType.Integer,
            descriptor.Schema.Fields.Single(field => field.Key == XmpSchema.Rating).Type);
    }

    private static async Task<MediaExtractionResult> ExtractAsync(byte[] data) =>
        await new XmpMetadataPlugin().ExtractAsync(new MediaItem(".xmp", null, data.Length),
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
