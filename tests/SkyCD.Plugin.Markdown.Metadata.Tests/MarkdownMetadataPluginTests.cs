using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Markdown.Metadata.Exceptions;
using Xunit;

namespace SkyCD.Plugin.Markdown.Metadata.Tests;

public class MarkdownMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsMarkdownAndAVersionedSchema()
    {
        var descriptor = new MarkdownMetadataPlugin().Descriptor;

        Assert.Equal([".md", ".markdown", ".mdown", ".mkd"], descriptor.Extensions);
        Assert.Equal(["text/markdown", "text/x-markdown"], descriptor.ContentTypes);
        Assert.Equal("skycd.markdown.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
    }

    [Fact]
    public async Task FrontMatter_IsPublishedFieldByField()
    {
        const string frontMatter = """
            Title: Quarterly Report
            description: Figures
            authors:
              - A. Writer
              - B. Writer
            tags: [finance, report]
            category: Finance
            lang: en
            date: 2024-03-04
            """;
        var result = await ExtractAsync(MarkdownFixtures.WithFrontMatter(frontMatter, "Body text."));

        Assert.Equal("markdown", Text(result, MarkdownSchema.Container));
        Assert.True(Boolean(result, MarkdownSchema.HasFrontMatter));
        Assert.True(Boolean(result, MarkdownSchema.FrontMatterValid));
        Assert.Equal("Quarterly Report", Text(result, MarkdownSchema.Title));
        Assert.Equal("Figures", Text(result, MarkdownSchema.Subject));
        Assert.Equal("A. Writer, B. Writer", Text(result, MarkdownSchema.Author));
        Assert.Equal("finance, report", Text(result, MarkdownSchema.Keywords));
        Assert.Equal("Finance", Text(result, MarkdownSchema.Category));
        Assert.Equal("en", Text(result, MarkdownSchema.Language));
        Assert.Equal(new DateTimeOffset(2024, 3, 4, 0, 0, 0, TimeSpan.Zero),
            Timestamp(result, MarkdownSchema.PublishedAt));
        Assert.Equal("Body text.", Text(result, MarkdownSchema.TextExcerpt));
    }

    [Fact]
    public async Task MissingFrontMatter_IsReportedWithoutInventingFields()
    {
        var result = await ExtractAsync(MarkdownFixtures.Text("# Heading\n\nBody text"));

        Assert.False(Boolean(result, MarkdownSchema.HasFrontMatter));
        Assert.DoesNotContain(result.Values, value => value.Key == MarkdownSchema.FrontMatterValid);
        Assert.DoesNotContain(result.Values, value => value.Key == MarkdownSchema.Title);
        Assert.Equal("Heading", Text(result, MarkdownSchema.FirstHeading));
        Assert.Equal(1L, Integer(result, MarkdownSchema.HeadingCount));
        Assert.Equal(3L, Integer(result, MarkdownSchema.WordCount));
        Assert.Equal(17L, Integer(result, MarkdownSchema.CharacterCount));
    }

    [Fact]
    public async Task DamagedFrontMatter_DoesNotLoseTheBody()
    {
        var result = await ExtractAsync(MarkdownFixtures.WithFrontMatter("title: [unclosed", "# Still here"));

        Assert.True(Boolean(result, MarkdownSchema.HasFrontMatter));
        Assert.False(Boolean(result, MarkdownSchema.FrontMatterValid));
        Assert.Null(Text(result, MarkdownSchema.Title));
        Assert.Equal("Still here", Text(result, MarkdownSchema.FirstHeading));
        Assert.Contains("Still here", Text(result, MarkdownSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnterminatedDelimiter_IsAThematicBreakNotFrontMatter()
    {
        var result = await ExtractAsync(MarkdownFixtures.Text("---\ntitle: x\nBody text"));

        Assert.False(Boolean(result, MarkdownSchema.HasFrontMatter));
        Assert.Null(Text(result, MarkdownSchema.Title));
        Assert.Contains("title: x", Text(result, MarkdownSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedFrontMatter_IsReportedInvalidWithoutPublishingFields()
    {
        var plugin = new MarkdownMetadataPlugin(new MarkdownPluginOptions(maxFrontMatterCharacters: 10));

        var result = await plugin.ExtractAsync(
            new MediaItem(".md", "text/markdown", 100),
            new ByteArrayMediaSource(MarkdownFixtures.WithFrontMatter("title: A Long Title", "Body")),
            progress: null,
            CancellationToken.None);

        Assert.True(Boolean(result, MarkdownSchema.HasFrontMatter));
        Assert.False(Boolean(result, MarkdownSchema.FrontMatterValid));
        Assert.Null(Text(result, MarkdownSchema.Title));
        Assert.Equal("Body", Text(result, MarkdownSchema.TextExcerpt));
    }

    [Fact]
    public async Task NestedFrontMatterMapping_IsNotFlattenedIntoAString()
    {
        const string frontMatter = """
            title: X
            author:
              name: A. Writer
            """;
        var result = await ExtractAsync(MarkdownFixtures.WithFrontMatter(frontMatter, "Body"));

        Assert.Equal("X", Text(result, MarkdownSchema.Title));
        Assert.Null(Text(result, MarkdownSchema.Author));
    }

    [Fact]
    public async Task MetadataValues_AreBounded()
    {
        var plugin = new MarkdownMetadataPlugin(new MarkdownPluginOptions(maxMetadataValueCharacters: 5));

        var result = await plugin.ExtractAsync(
            new MediaItem(".md", "text/markdown", 100),
            new ByteArrayMediaSource(MarkdownFixtures.WithFrontMatter("title: LongerTitle", "Body")),
            progress: null,
            CancellationToken.None);

        Assert.Equal("Longe", Text(result, MarkdownSchema.Title));
    }

    [Fact]
    public async Task Structure_IsCountedFromTheDocument()
    {
        const string markdown = """
            # Title

            Intro with [link](https://example.com) and ![image](https://example.com/a.png).

            ## Section

            - item one
            - item two

            ```csharp
            var x = 1;
            ```
            """;
        var result = await ExtractAsync(MarkdownFixtures.Text(markdown));

        Assert.Equal("Title", Text(result, MarkdownSchema.FirstHeading));
        Assert.Equal(2L, Integer(result, MarkdownSchema.HeadingCount));
        Assert.Equal(1L, Integer(result, MarkdownSchema.LinkCount));
        Assert.Equal(1L, Integer(result, MarkdownSchema.ImageCount));
        Assert.Equal(1L, Integer(result, MarkdownSchema.CodeBlockCount));
        Assert.True(Integer(result, MarkdownSchema.WordCount) > 5);
        Assert.Contains("Intro with link", Text(result, MarkdownSchema.TextExcerpt), StringComparison.Ordinal);
        Assert.Contains("item one", Text(result, MarkdownSchema.TextExcerpt), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlainText_IgnoresMarkupAndDrivesWordCount()
    {
        var result = await ExtractAsync(MarkdownFixtures.Text("Hello **world**"));

        Assert.Equal(2L, Integer(result, MarkdownSchema.WordCount));
        Assert.Equal(11L, Integer(result, MarkdownSchema.CharacterCount));
        Assert.Equal("Hello world", Text(result, MarkdownSchema.TextExcerpt));
        Assert.False(Boolean(result, MarkdownSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task TextExcerpt_IsCutAtTheBoundAndTruncationIsReported()
    {
        var plugin = new MarkdownMetadataPlugin(new MarkdownPluginOptions(maxTextExcerptCharacters: 10));
        var source = MarkdownFixtures.Text("The quick brown fox jumps over the lazy dog");

        var result = await plugin.ExtractAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

        Assert.Equal("The quick ", Text(result, MarkdownSchema.TextExcerpt));
        Assert.True(Boolean(result, MarkdownSchema.TextExcerptTruncated));
    }

    [Fact]
    public async Task TextlessDocument_PublishesZeroCountsAndNoExcerpt()
    {
        var result = await ExtractAsync(MarkdownFixtures.WithFrontMatter("title: Empty note", string.Empty));

        Assert.True(Boolean(result, MarkdownSchema.HasFrontMatter));
        Assert.Equal(0L, Integer(result, MarkdownSchema.WordCount));
        Assert.Equal(0L, Integer(result, MarkdownSchema.CharacterCount));
        Assert.Equal(0L, Integer(result, MarkdownSchema.HeadingCount));
        Assert.DoesNotContain(result.Values, value => value.Key == MarkdownSchema.TextExcerpt);
        Assert.DoesNotContain(result.Values, value => value.Key == MarkdownSchema.TextExcerptTruncated);
    }

    [Fact]
    public async Task Utf16WithByteOrderMark_IsDecoded()
    {
        var result = await ExtractAsync(MarkdownFixtures.Utf16("# Héllo"));

        Assert.Equal("Héllo", Text(result, MarkdownSchema.FirstHeading));
    }

    [Fact]
    public async Task InvalidUtf8_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<MarkdownDecodeException>(() => ExtractAsync(MarkdownFixtures.InvalidUtf8()));
    }

    [Fact]
    public async Task BinarySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<MarkdownDecodeException>(() => ExtractAsync(MarkdownFixtures.WithNullByte()));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<MarkdownDecodeException>(() => ExtractAsync([]));
    }

    [Fact]
    public async Task OversizedSource_IsRejectedBeforeAnyByteIsRead()
    {
        var source = MarkdownFixtures.Text("# Heading");
        var mediaSource = new ByteArrayMediaSource(source);
        var plugin = new MarkdownMetadataPlugin(new MarkdownPluginOptions(maxSourceBytes: source.Length - 1));

        await Assert.ThrowsAsync<MarkdownSourceTooLargeException>(() => plugin.ExtractAsync(
            new MediaItem(".md", "text/markdown", source.Length), mediaSource, null, CancellationToken.None));

        Assert.Equal(0, mediaSource.ChargedBytes);
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var source = MarkdownFixtures.Text("# Heading");
        var mediaSource = new ByteArrayMediaSource(source);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new MarkdownMetadataPlugin().ExtractAsync(
            new MediaItem(".md", "text/markdown", source.Length), mediaSource, null, cancelled.Token));

        Assert.Equal(0, mediaSource.ChargedBytes);
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = MarkdownSchema.Metadata;
        var result = await ExtractAsync(MarkdownFixtures.WithFrontMatter(
            "title: Title\nauthor: Author\ndate: 2020-01-02", "# Heading\n\nBody"));

        Assert.NotEmpty(result.Values);
        foreach (var value in result.Values)
        {
            var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
            Assert.NotNull(field);
            Assert.Equal(field!.Type, value.Type);
        }

        Assert.Equal(result.Values.Count,
            result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var source = MarkdownFixtures.WithFrontMatter("title: Immutability", "Body");
        var before = (byte[])source.Clone();

        await ExtractAsync(source);

        Assert.Equal(before, source);
    }

    [Fact]
    public async Task ReadingChargesTheSourceExactlyOnce()
    {
        var source = MarkdownFixtures.Text("# Bounded");
        var mediaSource = new ByteArrayMediaSource(source);

        await new MarkdownMetadataPlugin().ExtractAsync(
            new MediaItem(".md", "text/markdown", source.Length), mediaSource, null, CancellationToken.None);

        Assert.Equal(source.Length, mediaSource.ChargedBytes);
        Assert.True(mediaSource.ReadCount < 16,
            $"A {source.Length} byte document took {mediaSource.ReadCount} host reads.");
    }

    private static Task<MediaExtractionResult> ExtractAsync(byte[] source) =>
        new MarkdownMetadataPlugin().ExtractAsync(
            new MediaItem(".md", "text/markdown", source.Length),
            new ByteArrayMediaSource(source),
            progress: null,
            CancellationToken.None);

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    private static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    private static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;

    private static DateTimeOffset? Timestamp(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Timestamp;
}
