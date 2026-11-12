using System;
using Avalonia.Headless.XUnit;
using SkyCD.Formatting.Rtf;
using SkyCD.UI.Controls.RichText;
using Xunit;

namespace SkyCD.UI.Tests;

public class RichTextEditorTests
{
    [AvaloniaFact]
    public void RichTextEditor_InitializesEmpty()
    {
        var editor = new RichTextEditor();
        Assert.Equal(string.Empty, editor.RtfText);
    }

    [AvaloniaFact]
    public void RichTextEditor_SetPlainText_BuildsDocumentWithoutFormatting()
    {
        var editor = new RichTextEditor();
        editor.RtfText = "Simple plain comment";

        var doc = editor.BuildDocument();
        Assert.Single(doc.Paragraphs);
        Assert.Equal("Simple plain comment", doc.Paragraphs[0].PlainText);
        Assert.False(doc.HasFormatting);
    }

    [AvaloniaFact]
    public void RichTextEditor_SetRtf_BuildsDocumentWithFormatting()
    {
        var editor = new RichTextEditor();
        editor.RtfText = @"{\rtf1\ansi\deff0 {\b Bold text} and normal\par}";

        var doc = editor.BuildDocument();
        Assert.Single(doc.Paragraphs);
        Assert.Contains(doc.Paragraphs[0].Spans, s => s.Bold && s.Text.Contains("Bold text", StringComparison.Ordinal));
        Assert.True(doc.HasFormatting);
    }

    [AvaloniaFact]
    public void RichTextEditor_ToggleFormatting_UpdatesDocumentAndRtf()
    {
        var editor = new RichTextEditor();
        editor.RtfText = "Hello World";

        // Select "World"
        editor.SelectAll();
        editor.ToggleBold();

        var doc = editor.BuildDocument();
        Assert.True(doc.HasFormatting);
        Assert.Contains("{\\rtf1", editor.RtfText, StringComparison.OrdinalIgnoreCase);

        // Toggle bold again -> unbold
        editor.ToggleBold();
        var doc2 = editor.BuildDocument();
        Assert.False(doc2.HasFormatting);
    }

    [AvaloniaFact]
    public void RichTextEditor_ToggleBullet_TogglesBulletList()
    {
        var editor = new RichTextEditor();
        editor.RtfText = "First item\nSecond item";

        editor.SelectAll();
        editor.ToggleBullet();

        var doc = editor.BuildDocument();
        Assert.True(doc.Paragraphs[0].IsBullet);
        Assert.True(doc.Paragraphs[1].IsBullet);

        // Toggle bullet again -> removes bullets
        editor.ToggleBullet();
        var doc2 = editor.BuildDocument();
        Assert.False(doc2.Paragraphs[0].IsBullet);
        Assert.False(doc2.Paragraphs[1].IsBullet);
    }

    [AvaloniaFact]
    public void RichTextEditor_UndoRedo_RevertsAndRestoresState()
    {
        var editor = new RichTextEditor();
        editor.RtfText = "Original";

        editor.SelectAll();
        editor.ToggleItalic();

        Assert.True(editor.BuildDocument().HasFormatting);

        editor.Undo();
        Assert.False(editor.BuildDocument().HasFormatting);

        editor.Redo();
        Assert.True(editor.BuildDocument().HasFormatting);
    }

    [AvaloniaFact]
    public void RichTextEditor_MalformedRtf_SafelyLoadsWithoutCrashing()
    {
        var editor = new RichTextEditor();
        editor.RtfText = @"{\rtf1\ansi\b Broken RTF without closing";

        var doc = editor.BuildDocument();
        Assert.NotNull(doc);
        Assert.Contains("Broken RTF", doc.ToPlainText(), StringComparison.Ordinal);
    }
}
