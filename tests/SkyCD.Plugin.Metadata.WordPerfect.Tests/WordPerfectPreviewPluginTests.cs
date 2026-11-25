using System;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.WordPerfect.Export;
using SkyCD.Plugin.Metadata.WordPerfect.Models;

namespace SkyCD.Plugin.Metadata.WordPerfect.Tests;

public class WordPerfectPreviewPluginTests
{
    [Fact]
    public void Descriptor_ClaimsWordPerfectDocumentsAndAVersionedPreviewSchema()
    {
        var descriptor = new WordPerfectPreviewPlugin().Descriptor;

        Assert.Equal([".wpd", ".wpt"], descriptor.Extensions);
        Assert.Equal(["application/vnd.wordperfect"], descriptor.ContentTypes);
        Assert.Equal(WordPerfectSchema.PreviewSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task RenderedBody_ProducesABoundedPng()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Quarterly operations summary for the regional offices."),
            [0xC7],
            WordPerfectFixtures.Text("Second paragraph with enough words to wrap onto another line."));
        var document = WordPerfectFixtures.DocumentWithBody(body);
        var plugin = new WordPerfectPreviewPlugin(new WordPerfectPluginOptions(maxThumbnailDimension: 320));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".wpd", "application/vnd.wordperfect", document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);

        var bytes = artifact.Data.ToArray();
        Assert.True(bytes.Length > 8);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes[..8]);

        using var bitmap = SKBitmap.Decode(bytes);
        Assert.NotNull(bitmap);
        Assert.Equal(320, bitmap.Width);
        Assert.InRange(bitmap.Height, 321, 480);
    }

    [Fact]
    public async Task RecoveredText_IsBoundedByTheConfiguredCharacterCount()
    {
        var body = WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Hello"),
            [0xC7],
            WordPerfectFixtures.Text("World"));
        var document = WordPerfectFixtures.DocumentWithBody(body);
        var renderer = new StubRenderer(available: true, image: null);
        var plugin = new WordPerfectPreviewPlugin(
            new WordPerfectPluginOptions(maxThumbnailDimension: 128, maxRenderedCharacters: 6),
            renderer);

        await plugin.GeneratePreviewAsync(
            new MediaItem(".wpd", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        Assert.Equal("Hello", renderer.Text);
        Assert.Equal(128, renderer.MaxDimension);
        Assert.Equal(6, renderer.MaxCharacters);
    }

    [Fact]
    public async Task TextlessDocument_ProducesNoArtifact()
    {
        var document = WordPerfectFixtures.DocumentWithBody([]);
        var renderer = new StubRenderer(available: true, image: [1, 2, 3]);

        var result = await new WordPerfectPreviewPlugin(WordPerfectPluginOptions.Default, renderer)
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Null(renderer.Text);
    }

    [Fact]
    public async Task WordPerfectFiveDocument_ProducesNoArtifact()
    {
        var document = WordPerfectFixtures.WithSummary(
            [], majorVersion: 0, minorVersion: 0, body: WordPerfectFixtures.Text("Unsupported layout"));
        var renderer = new StubRenderer(available: true, image: [1, 2, 3]);

        var result = await new WordPerfectPreviewPlugin(WordPerfectPluginOptions.Default, renderer)
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Null(renderer.Text);
    }

    [Fact]
    public async Task MacintoshDocument_ProducesNoArtifact()
    {
        var document = WordPerfectFixtures.WithSummary(
            [], fileType: 0x2C, body: WordPerfectFixtures.Text("Unsupported layout"));

        var result = await new WordPerfectPreviewPlugin().GeneratePreviewAsync(
            new MediaItem(".wpd", null, document.Length),
            new ByteArrayMediaSource(document),
            progress: null,
            CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task ProtectedDocument_ReportsAuthenticationRequired()
    {
        var document = WordPerfectFixtures.WithSummary(
            [], encryption: 1, body: WordPerfectFixtures.Text("Secret"));

        await Assert.ThrowsAsync<WordPerfectAuthenticationRequiredException>(
            () => new WordPerfectPreviewPlugin().GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task NonWordPerfectSource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<WordPerfectDecodeException>(() => new WordPerfectPreviewPlugin()
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, 4),
                new ByteArrayMediaSource("nope"u8.ToArray()),
                progress: null,
                CancellationToken.None));
    }

    [Fact]
    public async Task MissingRenderer_LeavesTheSourceUnread()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));
        var source = new ByteArrayMediaSource(document);
        var plugin = new WordPerfectPreviewPlugin(
            WordPerfectPluginOptions.Default, new StubRenderer(available: false, image: null));

        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".wpd", null, document.Length), source, null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task RendererWithoutAnImage_ProducesNoArtifact()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));

        var result = await new WordPerfectPreviewPlugin(
                WordPerfectPluginOptions.Default, new StubRenderer(available: true, image: null))
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task RendererOutput_IsPublishedAsAPngArtifact()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var result = await new WordPerfectPreviewPlugin(
                WordPerfectPluginOptions.Default, new StubRenderer(available: true, image: image))
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(image, artifact.Data.ToArray());
    }

    [Fact]
    public async Task CorruptGroup_KeepsOnlyTheRecoveredText()
    {
        var corrupt = WordPerfectFixtures.VariableGroup(0xD3, 0, WordPerfectFixtures.Text("hidden"));
        corrupt[2] = 0xFF;
        corrupt[3] = 0xFF;
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Body(
            WordPerfectFixtures.Text("Lead"),
            corrupt));
        var renderer = new StubRenderer(available: true, image: [9, 9, 9]);

        var result = await new WordPerfectPreviewPlugin(WordPerfectPluginOptions.Default, renderer)
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Single(result.Artifacts);
        Assert.Equal("Lead", renderer.Text);
    }

    [Fact]
    public async Task CompoundDocumentBody_IsRendered()
    {
        var document = WordPerfectFixtures.WrapInCompoundDocument(
            WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Wrapped text")));
        var renderer = new StubRenderer(available: true, image: [4, 5, 6]);

        var result = await new WordPerfectPreviewPlugin(WordPerfectPluginOptions.Default, renderer)
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress: null,
                CancellationToken.None);

        Assert.Single(result.Artifacts);
        Assert.Equal("Wrapped text", renderer.Text);
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyRead()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));
        var source = new ByteArrayMediaSource(document);

        await Assert.ThrowsAsync<WordPerfectSourceTooLargeException>(() =>
            new WordPerfectPreviewPlugin(new WordPerfectPluginOptions(maxSourceBytes: 100))
                .GeneratePreviewAsync(
                    new MediaItem(".wpd", null, document.Length), source, null, CancellationToken.None));

        Assert.Equal(0, source.ChargedBytes);
        Assert.Equal(0, source.ReadCount);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));
        var source = new ByteArrayMediaSource(document);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WordPerfectPreviewPlugin()
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task Source_IsReadOnceAndChargedExactlyOnce()
    {
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));
        var source = new ByteArrayMediaSource(document);

        await new WordPerfectPreviewPlugin(WordPerfectPluginOptions.Default,
                new StubRenderer(available: true, image: null))
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length), source, null, CancellationToken.None);

        Assert.Equal(document.Length, source.ChargedBytes);
        Assert.Equal(1, source.ReadCount);
    }

    [Fact]
    public async Task Progress_ReportsCompletion()
    {
        var progress = new CollectingProgress();
        var document = WordPerfectFixtures.DocumentWithBody(WordPerfectFixtures.Text("Hello"));

        await new WordPerfectPreviewPlugin(WordPerfectPluginOptions.Default,
                new StubRenderer(available: true, image: [1]))
            .GeneratePreviewAsync(
                new MediaItem(".wpd", null, document.Length),
                new ByteArrayMediaSource(document),
                progress,
                CancellationToken.None);

        Assert.NotEmpty(progress.Reports);
        Assert.Equal(1, progress.Reports[^1]);
    }

    private sealed class StubRenderer(bool available, byte[]? image) : IWordPerfectThumbnailRenderer
    {
        public string? Text { get; private set; }

        public int? MaxDimension { get; private set; }

        public int? MaxCharacters { get; private set; }

        public bool IsAvailable => available;

        public Task<byte[]?> RenderAsync(
            string text,
            int maxDimension,
            int maxCharacters,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Text = text;
            MaxDimension = maxDimension;
            MaxCharacters = maxCharacters;
            return Task.FromResult(image);
        }
    }

    private sealed class CollectingProgress : IProgress<MediaProgress>
    {
        public System.Collections.Generic.List<double> Reports { get; } = [];

        public void Report(MediaProgress value) => Reports.Add(value.Fraction);
    }
}
