using System;
using System.Text;

namespace SkyCD.Formatting.Rtf;

public static class RtfWriter
{
    public static string Write(RichTextDocument document)
    {
        if (document is null || document.Paragraphs.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append(@"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1033{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}");
        sb.AppendLine();
        sb.Append(@"{\*\generator SkyCD}\viewkind4\uc1");
        sb.AppendLine();

        for (var p = 0; p < document.Paragraphs.Count; p++)
        {
            var para = document.Paragraphs[p];
            if (para.IsBullet)
            {
                sb.Append(@"\pard{\pntext\f1\'b7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'b7}}\fi-360\li360\f0 ");
            }
            else
            {
                sb.Append(@"\pard\li0\f0 ");
            }

            var currentBold = false;
            var currentItalic = false;
            var currentUnderline = false;
            var currentStrikeout = false;

            foreach (var span in para.Spans)
            {
                if (string.IsNullOrEmpty(span.Text))
                {
                    continue;
                }

                if (span.Bold != currentBold)
                {
                    sb.Append(span.Bold ? @"\b " : @"\b0 ");
                    currentBold = span.Bold;
                }

                if (span.Italic != currentItalic)
                {
                    sb.Append(span.Italic ? @"\i " : @"\i0 ");
                    currentItalic = span.Italic;
                }

                if (span.Underline != currentUnderline)
                {
                    sb.Append(span.Underline ? @"\ul " : @"\ulnone ");
                    currentUnderline = span.Underline;
                }

                if (span.Strikeout != currentStrikeout)
                {
                    sb.Append(span.Strikeout ? @"\strike " : @"\strike0 ");
                    currentStrikeout = span.Strikeout;
                }

                AppendEscapedText(sb, span.Text);
            }

            if (currentBold) sb.Append(@"\b0 ");
            if (currentItalic) sb.Append(@"\i0 ");
            if (currentUnderline) sb.Append(@"\ulnone ");
            if (currentStrikeout) sb.Append(@"\strike0 ");

            if (p < document.Paragraphs.Count - 1 || para.Spans.Count > 0 || para.IsBullet)
            {
                sb.Append(@"\par");
                sb.AppendLine();
            }
        }

        sb.Append('}');
        return sb.ToString();
    }

    private static void AppendEscapedText(StringBuilder sb, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append(@"\\");
                    break;
                case '{':
                    sb.Append(@"\{");
                    break;
                case '}':
                    sb.Append(@"\}");
                    break;
                case '\t':
                    sb.Append(@"\tab ");
                    break;
                case '\n':
                    sb.Append(@"\line ");
                    break;
                case '\r':
                    break;
                default:
                    if (ch > 127)
                    {
                        var code = (int)ch;
                        if (code > 32767)
                        {
                            code -= 65536;
                        }
                        sb.Append($@"\u{code}?");
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
    }
}
