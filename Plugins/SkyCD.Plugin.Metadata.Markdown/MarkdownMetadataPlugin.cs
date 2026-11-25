using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MarkdigMarkdown = Markdig.Markdown;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Markdown.Models;
using SkyCD.Plugin.Metadata.Markdown.Parsing;

namespace SkyCD.Plugin.Metadata.Markdown;

/// <summary>
/// Extracts YAML front matter, document structure and a bounded text excerpt from a Markdown file.
/// </summary>
/// <remarks>
/// The source is never modified and external references are never followed: the plugin holds bytes rather
/// than a path, and the parser turns links into counts and labels instead of fetching anything. Metadata the
/// document does not carry is omitted rather than published as an empty string, so "unknown" stays
/// distinguishable from "none". Markdown has no protection mechanism; an encrypted container that happens to
/// end in <c>.md</c> is simply not valid text and is reported as a decode error.
/// </remarks>
public sealed class MarkdownMetadataPlugin : IMediaMetadataCapability
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    private readonly MarkdownPluginOptions options;

    public MarkdownMetadataPlugin()
        : this(MarkdownPluginOptions.Default)
    {
    }

    public MarkdownMetadataPlugin(MarkdownPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(MarkdownSchema.Extensions, MarkdownSchema.ContentTypes, MarkdownSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var text = await MarkdownSourceReader
            .ReadTextAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new MediaProgress(0.4));

        var frontMatter = MarkdownFrontMatterReader.Read(
            text, options.MaxFrontMatterCharacters, options.MaxMetadataValueCharacters);
        var document = MarkdigMarkdown.Parse(frontMatter.Body, Pipeline);
        var plainText = CondensePlainText(MarkdigMarkdown.ToPlainText(frontMatter.Body, Pipeline));

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.7));

        var headings = document.Descendants<HeadingBlock>().ToList();
        var links = document.Descendants<LinkInline>().ToList();

        var values = new List<MediaMetadataValue>
        {
            new(MarkdownSchema.Container, MediaMetadataType.Text, Text: "markdown"),
            new(MarkdownSchema.HasFrontMatter, MediaMetadataType.Boolean, Boolean: frontMatter.Present),
            new(MarkdownSchema.HeadingCount, MediaMetadataType.Integer, Integer: headings.Count),
            new(MarkdownSchema.WordCount, MediaMetadataType.Integer, Integer: CountWords(plainText)),
            new(MarkdownSchema.CharacterCount, MediaMetadataType.Integer, Integer: plainText.Length),
            new(MarkdownSchema.LinkCount, MediaMetadataType.Integer,
                Integer: links.Count(link => !link.IsImage)),
            new(MarkdownSchema.ImageCount, MediaMetadataType.Integer,
                Integer: links.Count(link => link.IsImage)),
            new(MarkdownSchema.CodeBlockCount, MediaMetadataType.Integer,
                Integer: document.Descendants<CodeBlock>().Count())
        };

        if (frontMatter.Present)
        {
            values.Add(new MediaMetadataValue(MarkdownSchema.FrontMatterValid, MediaMetadataType.Boolean,
                Boolean: frontMatter.Valid));

            AddText(values, MarkdownSchema.Title, frontMatter.First("title"));
            AddText(values, MarkdownSchema.Subject, frontMatter.First("subject", "description", "summary"));
            AddText(values, MarkdownSchema.Author, frontMatter.First("author", "authors", "creator"));
            AddText(values, MarkdownSchema.Keywords, frontMatter.First("keywords", "tags"));
            AddText(values, MarkdownSchema.Category, frontMatter.First("category", "categories"));
            AddText(values, MarkdownSchema.Language, frontMatter.First("language", "lang"));
            AddTimestamp(values, MarkdownSchema.PublishedAt,
                ParseTimestamp(frontMatter.First("date", "published", "created")));
        }

        if (headings.Count > 0)
        {
            AddText(values, MarkdownSchema.FirstHeading, InlineText.Extract(headings[0].Inline));
        }

        AddTextExcerpt(values, plainText, cancellationToken);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    private static int CountWords(string text)
    {
        var count = 0;
        var inWord = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }

        return count;
    }

    /// <summary>Normalizes line endings, drops leading/trailing blank lines and collapses internal runs.</summary>
    private static string CondensePlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = new StringBuilder(text.Length);
        var pendingBlankLine = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0)
            {
                pendingBlankLine = builder.Length > 0;
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(pendingBlankLine ? "\n\n" : "\n");
            }

            builder.Append(line);
            pendingBlankLine = false;
        }

        return builder.ToString();
    }

    private void AddTextExcerpt(List<MediaMetadataValue> values, string text, CancellationToken cancellationToken)
    {
        if (text.Length == 0)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var truncated = text.Length > options.MaxTextExcerptCharacters;
        var length = truncated ? options.MaxTextExcerptCharacters : text.Length;
        if (truncated && char.IsHighSurrogate(text[length - 1]))
        {
            // Do not split a surrogate pair; a lone surrogate is not a valid stored string.
            length--;
        }

        var excerpt = text[..length];
        if (excerpt.Length == 0)
        {
            return;
        }

        values.Add(new MediaMetadataValue(MarkdownSchema.TextExcerpt, MediaMetadataType.Text, Text: excerpt));
        values.Add(new MediaMetadataValue(MarkdownSchema.TextExcerptTruncated, MediaMetadataType.Boolean,
            Boolean: truncated));
    }

    private void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var value = text.Trim();
        if (value.Length > options.MaxMetadataValueCharacters)
        {
            value = value[..options.MaxMetadataValueCharacters];
        }

        values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
    }

    private static void AddTimestamp(List<MediaMetadataValue> values, string key, DateTimeOffset? timestamp)
    {
        if (timestamp is { } value)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Timestamp, Timestamp: value));
        }
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
