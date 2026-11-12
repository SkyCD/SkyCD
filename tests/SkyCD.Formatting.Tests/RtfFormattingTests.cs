using System;
using System.Linq;
using SkyCD.Formatting.Rtf;
using Xunit;

namespace SkyCD.Formatting.Tests;

public class RtfFormattingTests
{
    [Fact]
    public void Read_PlainText_ThrowsInvalidRtfFormatException()
    {
        Assert.Throws<InvalidRtfFormatException>(() => RtfReader.Read("Just plain text"));
    }

    [Fact]
    public void FromRtfOrPlainText_PlainText_CreatesDocument()
    {
        var doc = RichTextDocument.FromRtfOrPlainText("Hello world\nSecond line");

        Assert.Equal(2, doc.Paragraphs.Count);
        Assert.Equal("Hello world", doc.Paragraphs[0].PlainText);
        Assert.Equal("Second line", doc.Paragraphs[1].PlainText);
        Assert.False(doc.HasFormatting);
    }

    [Fact]
    public void FromRtfOrPlainText_NullOrEmpty_ReturnsEmptyDocument()
    {
        var doc = RichTextDocument.FromRtfOrPlainText(null);
        Assert.NotNull(doc);
        Assert.Single(doc.Paragraphs);
        Assert.Equal(string.Empty, doc.ToPlainText());
    }

    [Fact]
    public void Read_FormattedRtf_ParsesBoldItalicUnderlineStrikeout()
    {
        var rtf = @"{\rtf1\ansi\deff0 {\b Bold text} {\i Italic text} {\ul Underlined} {\strike Strikethrough}\par}";
        var doc = RtfReader.Read(rtf);

        Assert.Single(doc.Paragraphs);
        var spans = doc.Paragraphs[0].Spans;

        Assert.Contains(spans, s => s.Text.Contains("Bold text", StringComparison.Ordinal) && s.Bold);
        Assert.Contains(spans, s => s.Text.Contains("Italic text", StringComparison.Ordinal) && s.Italic);
        Assert.Contains(spans, s => s.Text.Contains("Underlined", StringComparison.Ordinal) && s.Underline);
        Assert.Contains(spans, s => s.Text.Contains("Strikethrough", StringComparison.Ordinal) && s.Strikeout);
    }

    [Fact]
    public void Read_MixedCombinedFormatting_ParsesCorrectly()
    {
        var rtf = @"{\rtf1\ansi\deff0 \b\i Bold and Italic\i0\b0  Normal text\par}";
        var doc = RtfReader.Read(rtf);

        Assert.Single(doc.Paragraphs);
        var spans = doc.Paragraphs[0].Spans;

        Assert.True(spans.Count >= 2);
        Assert.True(spans[0].Bold && spans[0].Italic);
        Assert.Equal("Bold and Italic", spans[0].Text);
        Assert.False(spans[1].Bold || spans[1].Italic);
        Assert.Equal(" Normal text", spans[1].Text);
    }

    [Fact]
    public void Read_LegacyBulletList_IdentifiesBullets()
    {
        var rtf = @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}\pard{\pntext\f1\'b7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'b7}}\fi-360\li360\f0 First bullet\par{\pntext\f1\'b7\tab}Second bullet\par\pard\li0\f0 Normal line\par}";
        var doc = RtfReader.Read(rtf);

        Assert.Equal(3, doc.Paragraphs.Count);
        Assert.True(doc.Paragraphs[0].IsBullet);
        Assert.Equal("First bullet", doc.Paragraphs[0].PlainText);
        Assert.True(doc.Paragraphs[1].IsBullet);
        Assert.Equal("Second bullet", doc.Paragraphs[1].PlainText);
        Assert.False(doc.Paragraphs[2].IsBullet);
        Assert.Equal("Normal line", doc.Paragraphs[2].PlainText);
    }

    [Fact]
    public void Read_UnicodeAndEscapedCharacters_DecodesProperly()
    {
        var rtf = @"{\rtf1\ansi\deff0 Special chars: \{ \} \\ \u8226? bullet\par}";
        var doc = RtfReader.Read(rtf);

        Assert.Single(doc.Paragraphs);
        var text = doc.Paragraphs[0].PlainText;
        Assert.Contains("{", text, StringComparison.Ordinal);
        Assert.Contains("}", text, StringComparison.Ordinal);
        Assert.Contains("\\", text, StringComparison.Ordinal);
        Assert.Contains("•", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FromRtfOrPlainText_MalformedRtf_FallsBackGracefully()
    {
        var malformedRtf = @"{\rtf1\ansi\b Truncated text without closing bracket";
        var doc = RichTextDocument.FromRtfOrPlainText(malformedRtf);

        Assert.NotNull(doc);
        Assert.NotEmpty(doc.Paragraphs);
        Assert.Contains("Truncated text", doc.ToPlainText(), StringComparison.Ordinal);
    }

    [Fact]
    public void RoundTrip_FormattedDocument_PreservesAllFormatting()
    {
        var original = new RichTextDocument(
        [
            new RichTextParagraph(
            [
                new RichTextSpan("Heading: ", bold: true),
                new RichTextSpan("Some details in "),
                new RichTextSpan("italic", italic: true),
                new RichTextSpan(" and "),
                new RichTextSpan("underline", underline: true),
                new RichTextSpan(" and "),
                new RichTextSpan("strike", strikeout: true),
                new RichTextSpan(".")
            ]),
            new RichTextParagraph(
            [
                new RichTextSpan("Bullet point 1", bold: true)
            ], isBullet: true),
            new RichTextParagraph(
            [
                new RichTextSpan("Bullet point 2")
            ], isBullet: true),
            new RichTextParagraph(
            [
                new RichTextSpan("Ending paragraph.")
            ])
        ]);

        var rtf = original.ToRtf();
        Assert.StartsWith("{\\rtf1", rtf, StringComparison.OrdinalIgnoreCase);

        var restored = RichTextDocument.FromRtf(rtf);

        Assert.Equal(original.Paragraphs.Count, restored.Paragraphs.Count);

        for (var i = 0; i < original.Paragraphs.Count; i++)
        {
            var origPara = original.Paragraphs[i];
            var restPara = restored.Paragraphs[i];

            Assert.Equal(origPara.IsBullet, restPara.IsBullet);
            Assert.Equal(origPara.PlainText, restPara.PlainText);

            origPara.Normalize();
            restPara.Normalize();

            Assert.Equal(origPara.Spans.Count, restPara.Spans.Count);
            for (var s = 0; s < origPara.Spans.Count; s++)
            {
                Assert.Equal(origPara.Spans[s].Text, restPara.Spans[s].Text);
                Assert.Equal(origPara.Spans[s].Bold, restPara.Spans[s].Bold);
                Assert.Equal(origPara.Spans[s].Italic, restPara.Spans[s].Italic);
                Assert.Equal(origPara.Spans[s].Underline, restPara.Spans[s].Underline);
                Assert.Equal(origPara.Spans[s].Strikeout, restPara.Spans[s].Strikeout);
            }
        }
    }
}
