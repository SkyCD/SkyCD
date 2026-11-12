using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SkyCD.Formatting.Rtf;

public static class RtfReader
{
    private sealed class ParsingState
    {
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikeout { get; set; }
        public bool SkipDestination { get; set; }
        public bool InPnText { get; set; }
        public int UnicodeSkipCount { get; set; } = 1;

        public ParsingState Clone()
        {
            return new ParsingState
            {
                Bold = Bold,
                Italic = Italic,
                Underline = Underline,
                Strikeout = Strikeout,
                SkipDestination = SkipDestination,
                InPnText = InPnText,
                UnicodeSkipCount = UnicodeSkipCount
            };
        }
    }

    public static RichTextDocument Read(string rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf))
        {
            return new RichTextDocument();
        }

        var trimmed = rtf.TrimStart();
        if (!trimmed.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidRtfFormatException("Content does not begin with RTF signature '{\\rtf'.");
        }

        try
        {
            return ParseInternal(rtf);
        }
        catch (InvalidRtfFormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidRtfFormatException("Failed to parse RTF content.", ex);
        }
    }

    public static bool TryRead(string? rtf, out RichTextDocument document)
    {
        if (string.IsNullOrWhiteSpace(rtf))
        {
            document = new RichTextDocument();
            return true;
        }

        var trimmed = rtf.TrimStart();
        if (!trimmed.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase))
        {
            document = RichTextDocument.FromPlainText(rtf);
            return false;
        }

        try
        {
            document = ParseInternal(rtf);
            return true;
        }
        catch
        {
            document = RichTextDocument.FromPlainText(ExtractPlainTextFallback(rtf));
            return false;
        }
    }

    private static RichTextDocument ParseInternal(string rtf)
    {
        var document = new RichTextDocument();
        var stateStack = new Stack<ParsingState>();
        var currentState = new ParsingState();

        var currentParagraph = new RichTextParagraph();
        var currentSpanText = new StringBuilder();
        var currentSpanBold = false;
        var currentSpanItalic = false;
        var currentSpanUnderline = false;
        var currentSpanStrikeout = false;

        void FlushCurrentSpan()
        {
            if (currentSpanText.Length > 0)
            {
                currentParagraph.Spans.Add(new RichTextSpan(
                    currentSpanText.ToString(),
                    currentSpanBold,
                    currentSpanItalic,
                    currentSpanUnderline,
                    currentSpanStrikeout));
                currentSpanText.Clear();
            }
        }

        void FlushParagraph()
        {
            FlushCurrentSpan();
            currentParagraph.Normalize();
            document.Paragraphs.Add(currentParagraph);
            currentParagraph = new RichTextParagraph();
        }

        void EnsureSpanFormatting()
        {
            if (currentSpanText.Length > 0 &&
                (currentSpanBold != currentState.Bold ||
                 currentSpanItalic != currentState.Italic ||
                 currentSpanUnderline != currentState.Underline ||
                 currentSpanStrikeout != currentState.Strikeout))
            {
                FlushCurrentSpan();
            }

            currentSpanBold = currentState.Bold;
            currentSpanItalic = currentState.Italic;
            currentSpanUnderline = currentState.Underline;
            currentSpanStrikeout = currentState.Strikeout;
        }

        var index = 0;
        var length = rtf.Length;

        while (index < length)
        {
            var ch = rtf[index];

            if (ch == '{')
            {
                stateStack.Push(currentState.Clone());
                index++;
            }
            else if (ch == '}')
            {
                if (stateStack.Count > 0)
                {
                    currentState = stateStack.Pop();
                }
                index++;
            }
            else if (ch == '\\')
            {
                index++;
                if (index >= length)
                {
                    break;
                }

                var nextCh = rtf[index];
                if (nextCh is '\\' or '{' or '}')
                {
                    if (!currentState.SkipDestination && !currentState.InPnText)
                    {
                        EnsureSpanFormatting();
                        currentSpanText.Append(nextCh);
                    }
                    index++;
                }
                else if (nextCh == '~')
                {
                    if (!currentState.SkipDestination && !currentState.InPnText)
                    {
                        EnsureSpanFormatting();
                        currentSpanText.Append(' ');
                    }
                    index++;
                }
                else if (nextCh == '_')
                {
                    if (!currentState.SkipDestination && !currentState.InPnText)
                    {
                        EnsureSpanFormatting();
                        currentSpanText.Append('-');
                    }
                    index++;
                }
                else if (nextCh == '*')
                {
                    currentState.SkipDestination = true;
                    index++;
                }
                else if (nextCh == '\'')
                {
                    index++;
                    if (index + 1 < length &&
                        int.TryParse(rtf.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexVal))
                    {
                        index += 2;
                        if (!currentState.SkipDestination && !currentState.InPnText)
                        {
                            EnsureSpanFormatting();
                            var decodedChar = DecodeAnsiByte((byte)hexVal);
                            currentSpanText.Append(decodedChar);
                        }
                    }
                }
                else if (char.IsLetter(nextCh))
                {
                    var wordStart = index;
                    while (index < length && char.IsLetter(rtf[index]))
                    {
                        index++;
                    }

                    var word = rtf[wordStart..index];

                    int? param = null;
                    var hasParam = false;
                    var sign = 1;
                    if (index < length && rtf[index] == '-')
                    {
                        sign = -1;
                        index++;
                    }

                    var numStart = index;
                    while (index < length && char.IsDigit(rtf[index]))
                    {
                        index++;
                    }

                    if (index > numStart && int.TryParse(rtf[numStart..index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNum))
                    {
                        param = parsedNum * sign;
                        hasParam = true;
                    }

                    if (index < length && rtf[index] == ' ')
                    {
                        index++;
                    }

                    switch (word.ToLowerInvariant())
                    {
                        case "fonttbl" or "colortbl" or "stylesheet" or "info" or "header" or "footer" or "generator":
                            currentState.SkipDestination = true;
                            break;

                        case "pntext":
                            currentState.InPnText = true;
                            currentParagraph.IsBullet = true;
                            break;

                        case "pnlvlblt" or "bullet":
                            currentParagraph.IsBullet = true;
                            break;

                        case "pard":
                            if (currentParagraph.Spans.Count > 0 || currentSpanText.Length > 0)
                            {
                                FlushParagraph();
                            }
                            currentParagraph.IsBullet = false;
                            break;

                        case "par":
                            FlushParagraph();
                            break;

                        case "line":
                            if (!currentState.SkipDestination && !currentState.InPnText)
                            {
                                EnsureSpanFormatting();
                                currentSpanText.Append('\n');
                            }
                            break;

                        case "tab":
                            if (!currentState.SkipDestination && !currentState.InPnText)
                            {
                                EnsureSpanFormatting();
                                currentSpanText.Append('\t');
                            }
                            break;

                        case "b":
                            currentState.Bold = !hasParam || param != 0;
                            break;

                        case "i":
                            currentState.Italic = !hasParam || param != 0;
                            break;

                        case "ul" or "ulw" or "uld":
                            currentState.Underline = !hasParam || param != 0;
                            break;

                        case "ulnone" or "ul0":
                            currentState.Underline = false;
                            break;

                        case "strike":
                            currentState.Strikeout = !hasParam || param != 0;
                            break;

                        case "strike0":
                            currentState.Strikeout = false;
                            break;

                        case "uc":
                            currentState.UnicodeSkipCount = param ?? 1;
                            break;

                        case "u":
                            if (param.HasValue)
                            {
                                var unicodeChar = (char)(param.Value < 0 ? param.Value + 65536 : param.Value);
                                if (!currentState.SkipDestination && !currentState.InPnText)
                                {
                                    EnsureSpanFormatting();
                                    currentSpanText.Append(unicodeChar);
                                }

                                var skips = currentState.UnicodeSkipCount;
                                while (skips > 0 && index < length)
                                {
                                    if (rtf[index] == '\\' && index + 1 < length && rtf[index + 1] == '\'')
                                    {
                                        index += 4;
                                        skips--;
                                    }
                                    else if (rtf[index] is not '{' and not '}' and not '\\')
                                    {
                                        index++;
                                        skips--;
                                    }
                                    else
                                    {
                                        break;
                                    }
                                }
                            }
                            break;
                    }
                }
                else
                {
                    index++;
                }
            }
            else if (ch is '\r' or '\n')
            {
                index++;
            }
            else
            {
                if (!currentState.SkipDestination && !currentState.InPnText)
                {
                    EnsureSpanFormatting();
                    currentSpanText.Append(ch);
                }
                index++;
            }
        }

        FlushParagraph();

        while (document.Paragraphs.Count > 1 &&
               document.Paragraphs[^1].Spans.Count == 0 &&
               !document.Paragraphs[^1].IsBullet)
        {
            document.Paragraphs.RemoveAt(document.Paragraphs.Count - 1);
        }

        if (document.Paragraphs.Count == 0)
        {
            document.Paragraphs.Add(new RichTextParagraph());
        }

        return document;
    }

    private static char DecodeAnsiByte(byte b)
    {
        try
        {
            var bytes = new[] { b };
            var chars = Encoding.GetEncoding(1252).GetChars(bytes);
            return chars.Length > 0 ? chars[0] : (char)b;
        }
        catch
        {
            return (char)b;
        }
    }

    private static string ExtractPlainTextFallback(string rtf)
    {
        var sb = new StringBuilder();
        var index = 0;
        var length = rtf.Length;
        var depth = 0;
        var inDestination = false;

        while (index < length)
        {
            var ch = rtf[index];
            if (ch == '{')
            {
                depth++;
                index++;
            }
            else if (ch == '}')
            {
                depth = Math.Max(0, depth - 1);
                if (depth <= 1)
                {
                    inDestination = false;
                }
                index++;
            }
            else if (ch == '\\')
            {
                index++;
                if (index >= length) break;
                if (rtf[index] == '*')
                {
                    inDestination = true;
                    index++;
                }
                else if (char.IsLetter(rtf[index]))
                {
                    var wordStart = index;
                    while (index < length && char.IsLetter(rtf[index])) index++;
                    var word = rtf[wordStart..index].ToLowerInvariant();
                    while (index < length && (char.IsDigit(rtf[index]) || rtf[index] == '-')) index++;
                    if (index < length && rtf[index] == ' ') index++;

                    if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "generator")
                    {
                        inDestination = true;
                    }
                    else if (word is "par" && !inDestination)
                    {
                        sb.AppendLine();
                    }
                    else if (word is "tab" && !inDestination)
                    {
                        sb.Append('\t');
                    }
                }
                else
                {
                    if (!inDestination && rtf[index] is '\\' or '{' or '}')
                    {
                        sb.Append(rtf[index]);
                    }
                    index++;
                }
            }
            else if (ch is not '\r' and not '\n')
            {
                if (!inDestination)
                {
                    sb.Append(ch);
                }
                index++;
            }
            else
            {
                index++;
            }
        }

        return sb.ToString();
    }
}
