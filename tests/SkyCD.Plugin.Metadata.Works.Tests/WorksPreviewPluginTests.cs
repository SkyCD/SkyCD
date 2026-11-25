using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Works.Exceptions;
using SkyCD.Plugin.Metadata.Works.Export;
using SkyCD.Plugin.Metadata.Works.Models;
using Xunit;

namespace SkyCD.Plugin.Metadata.Works.Tests;

public class WorksPreviewPluginTests
{
    private const string Body =
        "Quarterly report for the regional sales meeting. Revenue grew by twelve percent this quarter, "
        + "and the field team expects the trend to continue into the next period.";

    private static readonly byte[] StubPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Descriptor_ClaimsOnlyWorksSourcesAndAVersionedPreviewSchema()
    {
        var descriptor = new WorksPreviewPlugin().Descriptor;

        Assert.Equal([".wps"], descriptor.Extensions);
        Assert.Equal(["application/vnd.ms-works"], descriptor.ContentTypes);
        Assert.Equal("skycd.works.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task TextBody_IsRenderedAsABoundedPagePng()
    {
        var document = WorksFixtures.Works4WithBody(Body);
        var plugin = new WorksPreviewPlugin(new WorksPluginOptions(maxThumbnailDimension: 320));

        var result = await plugin.GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);

        var bytes = artifact.Data.ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.InRange(bitmap.Height, 321, 480);
    }

    [Fact]
    public async Task NarrowBody_IsDecodedFromTheDocumentCodePage()
    {
        var renderer = new StubRenderer(available: true, image: StubPng);

        await PreviewAsync(WorksFixtures.Works4WithBody(Body), renderer);

        Assert.Contains("Quarterly report", renderer.RenderedText);
        Assert.Contains("regional sales meeting", renderer.RenderedText);
    }

    [Fact]
    public async Task NarrowBody_DecodesWindows1252Punctuation()
    {
        var body = "The “regional” report — prepared by the field team — covers the results and next steps.";
        var renderer = new StubRenderer(available: true, image: StubPng);

        await PreviewAsync(WorksFixtures.Works4WithBody(body), renderer);

        Assert.Contains("“regional”", renderer.RenderedText);
        Assert.Contains("— prepared", renderer.RenderedText);
    }

    [Fact]
    public async Task WideBody_IsDecodedFromUtf16()
    {
        var renderer = new StubRenderer(available: true, image: StubPng);

        await PreviewAsync(WorksFixtures.Works2000WithBody(Body), renderer);

        Assert.Contains("Quarterly report", renderer.RenderedText);
        Assert.DoesNotContain("CHNKINK", renderer.RenderedText);
    }

    [Fact]
    public async Task EmbeddedPngPreview_IsPublishedWithoutRendering()
    {
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        var renderer = new StubRenderer(available: true, image: null);

        var result = await PreviewAsync(WorksFixtures.Works4WithEmbeddedPreview(image), renderer);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(image, artifact.Data.ToArray());
        Assert.Equal(0, renderer.CallCount);
    }

    [Fact]
    public async Task EmbeddedJpegPreview_KeepsItsContentType()
    {
        var image = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 5, 6, 7 };
        var renderer = new StubRenderer(available: true, image: null);

        var result = await PreviewAsync(WorksFixtures.Works4WithEmbeddedPreview(image), renderer);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal(image, artifact.Data.ToArray());
    }

    [Fact]
    public async Task FlatWorksTwoDocument_ProducesNoArtifact()
    {
        var document = WorksFixtures.Flat(version: 2);

        var result = await new WorksPreviewPlugin().GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task BodyWithoutARecognizableTextRun_ProducesNoArtifact()
    {
        var document = WorksFixtures.Works4WithBody("Too short");

        var result = await new WorksPreviewPlugin().GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task MissingRenderer_LeavesTheSourceUnread()
    {
        var document = WorksFixtures.Works4WithBody(Body);
        var source = new ByteArrayMediaSource(document);
        var plugin = new WorksPreviewPlugin(WorksPluginOptions.Default,
            new StubRenderer(available: false, image: null));

        var result = await plugin.GeneratePreviewAsync(
            Item(document), source, progress: null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task RendererWithoutAnImage_ProducesNoArtifact()
    {
        var document = WorksFixtures.Works4WithBody(Body);
        var source = new ByteArrayMediaSource(document);
        var plugin = new WorksPreviewPlugin(WorksPluginOptions.Default,
            new StubRenderer(available: true, image: null));

        var result = await plugin.GeneratePreviewAsync(
            Item(document), source, progress: null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task DamagedBodyChain_FailsWithADecodeError()
    {
        var document = WorksFixtures.Works4WithDamagedBodyChain();

        await Assert.ThrowsAsync<WorksDecodeException>(() => new WorksPreviewPlugin().GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task EncryptedDocument_ReportsAuthenticationRequired()
    {
        var document = WorksFixtures.EncryptedWrapper();

        await Assert.ThrowsAsync<WorksAuthenticationRequiredException>(
            () => new WorksPreviewPlugin().GeneratePreviewAsync(
                Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task NonWorksSource_FailsWithADecodeError()
    {
        var document = WorksFixtures.NotWorks();

        await Assert.ThrowsAsync<WorksDecodeException>(() => new WorksPreviewPlugin().GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var document = WorksFixtures.Works4WithBody(Body);
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WorksPreviewPlugin().GeneratePreviewAsync(
            Item(document), source, progress: null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task ReadingChargesTheSourceOnce()
    {
        var document = WorksFixtures.Works4WithBody(Body);
        var source = new ByteArrayMediaSource(document);

        await new WorksPreviewPlugin().GeneratePreviewAsync(
            Item(document), source, progress: null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task TextSample_StaysWithinTheConfiguredCharacterBound()
    {
        var document = WorksFixtures.Works4WithBody(string.Join(' ', Enumerable.Repeat(Body, 40)));
        var renderer = new StubRenderer(available: true, image: StubPng);
        var plugin = new WorksPreviewPlugin(new WorksPluginOptions(maxTextCharacters: 80), renderer);

        var result = await plugin.GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None);

        Assert.Single(result.Artifacts);
        Assert.InRange(renderer.RenderedCharacters, 64, 80);
        Assert.Equal(80, renderer.MaxCharacters);
    }

    [Fact]
    public async Task Progress_IsReportedAndEndsAtCompletion()
    {
        var document = WorksFixtures.Works4WithBody(Body);
        var progress = new CollectingProgress();
        var plugin = new WorksPreviewPlugin(WorksPluginOptions.Default,
            new StubRenderer(available: true, image: StubPng));

        await plugin.GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress, CancellationToken.None);

        Assert.NotEmpty(progress.Values);
        Assert.Equal(1, progress.Values[^1]);
        Assert.All(progress.Values, value => Assert.InRange(value, 0, 1));
    }

    private static Task<MediaPreviewResult> PreviewAsync(byte[] document, IWorksThumbnailRenderer renderer) =>
        new WorksPreviewPlugin(WorksPluginOptions.Default, renderer).GeneratePreviewAsync(
            Item(document), new ByteArrayMediaSource(document), progress: null, CancellationToken.None);

    private static MediaItem Item(byte[] document) => new(".wps", "application/vnd.ms-works", document.Length);

    private sealed class StubRenderer(bool available, byte[]? image) : IWorksThumbnailRenderer
    {
        private readonly List<IReadOnlyList<string>> calls = [];

        public bool IsAvailable => available;

        public int CallCount => calls.Count;

        public int MaxCharacters { get; private set; }

        public string RenderedText =>
            string.Join("\n", calls.SelectMany(paragraphs => paragraphs));

        public int RenderedCharacters =>
            calls.SelectMany(paragraphs => paragraphs).Sum(paragraph => paragraph.Length);

        public Task<byte[]?> RenderAsync(
            IReadOnlyList<string> paragraphs,
            int maxDimension,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(paragraphs);
            MaxCharacters = maxCharacters;
            return Task.FromResult(image);
        }
    }

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public List<double> Values { get; } = [];

        public void Report(MediaProgress value) => Values.Add(value.Fraction);
    }
}
