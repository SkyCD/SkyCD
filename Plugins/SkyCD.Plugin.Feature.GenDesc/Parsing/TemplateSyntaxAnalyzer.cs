using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Feature.GenDesc.Parsing;

public enum TemplateSyntaxKind
{
    Text,
    Tag,
    Keyword,
    Function,
    Variable,
    String,
    Number,
    Comment,
    Operator,
    BbCode
}

public readonly record struct TemplateSyntaxSpan(int Start, int Length, TemplateSyntaxKind Kind);

/// <summary>
/// Classifies a template body into spans for the editor's syntax view. It deliberately mirrors the compiler's
/// grammar instead of guessing: comment and tag blocks, keywords, calls, strings, numbers, filters and BBCode.
/// </summary>
public static class TemplateSyntaxAnalyzer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "elseif", "else", "end"
    };

    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "|", "==", "!=", "&&", "||", ">", "<", ">=", "<=", "+", "-", "*", "/"
    };

    public static IReadOnlyList<TemplateSyntaxSpan> Analyze(string? body)
    {
        var text = body ?? string.Empty;
        var spans = new List<TemplateSyntaxSpan>();
        var index = 0;
        while (index < text.Length)
        {
            if (TryReadTag(text, index, spans, out var tagEnd))
            {
                index = tagEnd;
                continue;
            }

            if (TryReadBbCode(text, index, spans, out var bbCodeEnd))
            {
                index = bbCodeEnd;
                continue;
            }

            index++;
        }

        return spans;
    }

    private static bool TryReadTag(string text, int start, List<TemplateSyntaxSpan> spans, out int end)
    {
        end = start;
        var openEnd = start + 2;
        if (openEnd > text.Length || text[start] != '<' || text[start + 1] != '%')
        {
            return false;
        }

        var close = text.IndexOf("%>", openEnd, StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        end = close + 2;
        var trimmedStart = openEnd;
        while (trimmedStart < close && text[trimmedStart] == ' ')
        {
            trimmedStart++;
        }

        if (trimmedStart < close && (text[trimmedStart] == '#' || text[trimmedStart] == '-' && trimmedStart + 1 < close && text[trimmedStart + 1] == '#'))
        {
            spans.Add(new TemplateSyntaxSpan(start, end - start, TemplateSyntaxKind.Comment));
            return true;
        }

        spans.Add(new TemplateSyntaxSpan(start, 2, TemplateSyntaxKind.Tag));
        AnalyzeInsideTag(text, openEnd, close, spans);
        spans.Add(new TemplateSyntaxSpan(close, 2, TemplateSyntaxKind.Tag));
        return true;
    }

    private static void AnalyzeInsideTag(string text, int start, int end, List<TemplateSyntaxSpan> spans)
    {
        var index = start;
        while (index < end)
        {
            var c = text[index];
            if (char.IsWhiteSpace(c))
            {
                index++;
                continue;
            }

            if (c == '"')
            {
                var stringStart = index;
                index++;
                while (index < end)
                {
                    if (text[index] == '\\' && index + 1 < end)
                    {
                        index += 2;
                        continue;
                    }

                    if (text[index] == '"')
                    {
                        index++;
                        break;
                    }

                    index++;
                }

                spans.Add(new TemplateSyntaxSpan(stringStart, index - stringStart, TemplateSyntaxKind.String));
                continue;
            }

            if (char.IsDigit(c))
            {
                var numberStart = index;
                while (index < end && (char.IsDigit(text[index]) || text[index] == '.'))
                {
                    index++;
                }

                spans.Add(new TemplateSyntaxSpan(numberStart, index - numberStart, TemplateSyntaxKind.Number));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var nameStart = index;
                while (index < end && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                {
                    index++;
                }

                var name = text[nameStart..index];
                var lookahead = index;
                while (lookahead < end && char.IsWhiteSpace(text[lookahead]))
                {
                    lookahead++;
                }

                var kind = Keywords.Contains(name)
                    ? TemplateSyntaxKind.Keyword
                    : lookahead < end && text[lookahead] == '('
                        ? TemplateSyntaxKind.Function
                        : TemplateSyntaxKind.Variable;
                spans.Add(new TemplateSyntaxSpan(nameStart, index - nameStart, kind));
                continue;
            }

            var operatorEnd = index + 1;
            if (index + 1 < end && Operators.Contains(text.Substring(index, 2)))
            {
                operatorEnd = index + 2;
            }

            spans.Add(new TemplateSyntaxSpan(index, operatorEnd - index, TemplateSyntaxKind.Operator));
            index = operatorEnd;
        }
    }

    private static bool TryReadBbCode(string text, int start, List<TemplateSyntaxSpan> spans, out int end)
    {
        end = start;
        if (text[start] != '[')
        {
            return false;
        }

        var close = text.IndexOf(']', start + 1);
        if (close < 0)
        {
            return false;
        }

        var inner = text[(start + 1)..close];
        if (inner.Length == 0)
        {
            return false;
        }

        var nameStart = inner[0] == '/' ? 1 : 0;
        if (nameStart >= inner.Length || !char.IsLetter(inner[nameStart]))
        {
            return false;
        }

        foreach (var c in inner)
        {
            if (c is '[' or ']' or '\n' or '\r')
            {
                return false;
            }
        }

        end = close + 1;
        spans.Add(new TemplateSyntaxSpan(start, end - start, TemplateSyntaxKind.BbCode));
        return true;
    }
}
