using System;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Formatting.Rtf;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class RichTextCommentsIntegrationTests
{
    [Fact]
    public void PropertiesCommand_LoadsAndPreservesLegacyRtfComments()
    {
        var legacyRtf = @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}\pard{\pntext\f1\'b7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'b7}}\fi-360\li360\f0 {\b Important} item\par{\pntext\f1\'b7\tab}{\i Second} bullet\par\pard\li0\f0 Regular footer\par}";

        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        vm.SelectedTreeNode = vm.TreeNodes[0].Children.First(node => node.Key == "movies");
        vm.SelectedBrowserItem = vm.BrowserItems.First(item => item.Type != SkyCD.Documents.Enum.CatalogDocumentType.Folder);

        PropertiesDialogRequestedEventArgs? requested = null;
        vm.PropertiesRequested += (_, args) => requested = args;

        // Open properties
        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(requested);
        requested!.Dialog.Comments = legacyRtf;
        requested.Complete(true, requested.Dialog.Comments);

        Assert.True(vm.IsDirtyDocument);

        // Reopen properties to verify round-trip
        requested = null;
        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(legacyRtf, requested!.Dialog.Comments);

        // Parse with RichTextDocument to ensure valid parsing of legacy RTF
        var doc = RichTextDocument.FromRtfOrPlainText(requested.Dialog.Comments);
        Assert.Equal(3, doc.Paragraphs.Count);
        Assert.True(doc.Paragraphs[0].IsBullet);
        Assert.Contains(doc.Paragraphs[0].Spans, s => s.Bold && s.Text == "Important");
        Assert.True(doc.Paragraphs[1].IsBullet);
        Assert.Contains(doc.Paragraphs[1].Spans, s => s.Italic && s.Text == "Second");
        Assert.False(doc.Paragraphs[2].IsBullet);
        Assert.Equal("Regular footer", doc.Paragraphs[2].PlainText);

        // Edit comments with updated RTF
        var modifiedDoc = doc.Clone();
        modifiedDoc.Paragraphs[2].Spans[0].Underline = true;
        var updatedRtf = modifiedDoc.ToRtf();

        requested.Dialog.Comments = updatedRtf;
        requested.Complete(true, requested.Dialog.Comments);

        Assert.True(vm.IsDirtyDocument);

        // Reopen properties to verify round-trip
        requested = null;
        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(requested);
        Assert.Equal(updatedRtf, requested!.Dialog.Comments);

        var reloadedDoc = RichTextDocument.FromRtfOrPlainText(requested.Dialog.Comments);
        Assert.Equal(3, reloadedDoc.Paragraphs.Count);
        Assert.True(reloadedDoc.Paragraphs[2].Spans[0].Underline);
    }

    [Fact]
    public void PropertiesCommand_HandlesPlainTextCommentsSafely()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        vm.SelectedTreeNode = vm.TreeNodes[0].Children.First(node => node.Key == "music");
        vm.SelectedBrowserItem = vm.BrowserItems.First(item => item.Type != SkyCD.Documents.Enum.CatalogDocumentType.Folder);

        PropertiesDialogRequestedEventArgs? requested = null;
        vm.PropertiesRequested += (_, args) => requested = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(requested);
        requested!.Dialog.Comments = "Simple plain comment line 1\nLine 2";
        requested.Complete(true, requested.Dialog.Comments);

        var doc = RichTextDocument.FromRtfOrPlainText(requested.Dialog.Comments);
        Assert.Equal(2, doc.Paragraphs.Count);
        Assert.False(doc.HasFormatting);
    }

    [Fact]
    public void PropertiesCommand_HandlesMalformedRtfSafely()
    {
        var vm = MainWindowViewModel.CreateForInMemoryCatalog(TestCatalogEntries.Default());
        vm.SelectedTreeNode = vm.TreeNodes[0];
        vm.SelectedBrowserItem = vm.BrowserItems.First();

        PropertiesDialogRequestedEventArgs? requested = null;
        vm.PropertiesRequested += (_, args) => requested = args;

        vm.OpenPropertiesCommand.Execute(null);

        Assert.NotNull(requested);
        requested!.Dialog.Comments = @"{\rtf1\ansi\b Truncated raw markup";
        requested.Complete(true, requested.Dialog.Comments);

        var doc = RichTextDocument.FromRtfOrPlainText(requested.Dialog.Comments);
        Assert.NotNull(doc);
        Assert.Contains("Truncated raw markup", doc.ToPlainText(), StringComparison.Ordinal);
    }
}
