using System;
using System.Text;
using SkyCD.Plugin.Text.Exceptions;

namespace SkyCD.Plugin.Text;

/// <summary>The facts the plugin established about one text source.</summary>
internal sealed record TextAnalysis(
    string EncodingName,
    bool HasByteOrderMark,
    string LineEndings,
    int LineCount,
    int CharacterCount,
    bool ScanComplete,
    string Excerpt,
    bool ExcerptTruncated,
    string? Title);

/// <summary>
/// Turns a decoded window of a source into the facts the schema publishes.
/// </summary>
internal static class TextAnalyzer
{
    /// <summary>
    /// Decodes and analyses one window of a source.
    /// </summary>
    /// <param name="window">The leading bytes that were read, which may be shorter than the source.</param>
    /// <param name="scanComplete">True when the window is the whole source.</param>
    /// <param name="extension">Used only to categorize the allow-list entry and to decide whether a Markdown
    /// heading should be read as a title. It never changes how bytes are decoded.</param>
    public static TextAnalysis Analyze(
        ReadOnlySpan<byte> window,
        bool scanComplete,
        string extension,
        TextPluginOptions options)
    {
        var encoding = TextEncodingDetector.Detect(window);
        var payload = window[encoding.PreambleLength..];
        payload = TrimToCharacterBoundary(payload, encoding.Name);

        string text;
        try
        {
            text = encoding.Encoding.GetString(payload);
        }
        catch (DecoderFallbackException exception)
        {
            throw new TextDecodeException($"the payload is not valid {encoding.Name}", exception);
        }

        RejectBinaryContent(text, options.BinaryDetectionWindowBytes, options.MaxBinaryControlRatio);

        var (crlf, loneLf, loneCr) = CountLineEndings(text);
        var lineEndings = ClassifyLineEndings(crlf, loneLf, loneCr);
        var lineCount = CountLines(text, crlf + loneLf + loneCr);

        var truncated = text.Length > options.MaxExcerptCharacters || !scanComplete;
        var excerpt = text.Length > options.MaxExcerptCharacters
            ? text[..options.MaxExcerptCharacters]
            : text;

        return new TextAnalysis(
            encoding.Name,
            encoding.PreambleLength > 0,
            lineEndings,
            lineCount,
            text.Length,
            scanComplete,
            excerpt,
            truncated,
            ResolveTitle(extension, text));
    }

    /// <summary>
    /// Rejects a source whose decoded characters are mostly control codes. A byte-level check would reject
    /// every UTF-16 file, whose NUL bytes are legitimate, so this runs after decoding.
    /// </summary>
    private static void RejectBinaryContent(string text, int windowCharacters, double maxRatio)
    {
        if (text.Length == 0)
        {
            return;
        }

        var inspected = text.AsSpan(0, Math.Min(text.Length, windowCharacters));
        var controlCharacters = TextEncodingDetector.CountControlCharacters(inspected);
        if (controlCharacters > inspected.Length * maxRatio)
        {
            throw new BinaryContentRejectedException(controlCharacters, inspected.Length);
        }
    }

    private static (int Crlf, int LoneLf, int LoneCr) CountLineEndings(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\r' when i + 1 < text.Length && text[i + 1] == '\n':
                    crlf++;
                    i++;
                    break;
                case '\r':
                    cr++;
                    break;
                case '\n':
                    lf++;
                    break;
            }
        }

        return (crlf, lf, cr);
    }

    private static string ClassifyLineEndings(int crlf, int lf, int cr)
    {
        var kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        if (kinds == 0)
        {
            return "none";
        }

        if (kinds > 1)
        {
            return "mixed";
        }

        return crlf > 0 ? "crlf" : lf > 0 ? "lf" : "cr";
    }

    /// <summary>A trailing unterminated line still counts as a line, so a one-line file reports one.</summary>
    private static int CountLines(string text, int terminators)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var last = text[^1];
        var endsWithTerminator = last is '\n' or '\r';
        return terminators + (endsWithTerminator ? 0 : 1);
    }

    /// <summary>
    /// Reads a Markdown title from the first ATX heading. Only Markdown gets one: inferring a title from
    /// arbitrary source or CSV would be a guess, and a guess stored as a fact is worse than an absent field.
    /// </summary>
    private static string? ResolveTitle(string extension, string text)
    {
        if (!IsMarkdown(extension) || text.Length == 0)
        {
            return null;
        }

        var position = 0;
        while (position < text.Length)
        {
            var end = text.IndexOf('\n', position);
            if (end < 0)
            {
                end = text.Length;
            }

            var line = text[position..end].TrimEnd('\r').Trim();
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                var level = 0;
                while (level < line.Length && line[level] == '#')
                {
                    level++;
                }

                if (level is >= 1 and <= 6 && level < line.Length && char.IsWhiteSpace(line[level]))
                {
                    var title = line[level..].Trim();
                    if (title.Length > 0)
                    {
                        return title;
                    }
                }
            }

            position = end + 1;
        }

        return null;
    }

    /// <summary>
    /// Drops a trailing partial character, which a bounded window can cut in half. Without this a truncated
    /// UTF-8 sequence at the window edge would fail strict decoding and be misreported as Latin-1.
    /// </summary>
    internal static ReadOnlySpan<byte> TrimToCharacterBoundary(ReadOnlySpan<byte> payload, string encodingName)
    {
        switch (encodingName)
        {
            case "utf-16le" or "utf-16be":
                return payload.Length % 2 == 0 ? payload : payload[..^1];
            case "utf-32le" or "utf-32be":
                return payload.Length % 4 == 0 ? payload : payload[..(payload.Length - (payload.Length % 4))];
            case "utf-8":
                return TextEncodingDetector.TrimIncompleteUtf8(payload);
            default:
                return payload;
        }
    }

    /// <summary>Allow-list category, published so a consumer can filter without re-deriving it.</summary>
    internal static string Classify(string extension)
    {
        var normalized = (extension ?? string.Empty).ToLowerInvariant();
        if (IsMarkdown(normalized))
        {
            return "markdown";
        }

        return normalized switch
        {
            ".csv" or ".tsv" => "csv",
            ".txt" or ".text" or ".log" => "text",
            ".ini" or ".cfg" or ".conf" or ".properties" or ".yaml" or ".yml" or ".toml" or ".json" or ".xml" => "structured",
            _ => "source"
        };
    }

    private static bool IsMarkdown(string extension) =>
        (extension ?? string.Empty).ToLowerInvariant() is ".md" or ".markdown";
}
