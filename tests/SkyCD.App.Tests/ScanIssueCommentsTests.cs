using System;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

public class ScanIssueCommentsTests
{
    [Fact]
    public void Apply_AppendsIssuesToMatchedEntryAndKeepsExistingComment()
    {
        var document = CreateDocument("bad.bin", "/root/bad.bin", "User note");

        var changed = ScanIssueComments.Apply([document],
        [
            new ScanIssue(ScanIssueSeverity.Error, "/root/bad.bin", "Failed: Access denied"),
            new ScanIssue(ScanIssueSeverity.Warning, "/root/bad.bin", "Metadata unreadable")
        ]);

        var updated = Assert.Single(changed);
        Assert.Same(document, updated);
        Assert.Equal(
            $"User note{Environment.NewLine}Failed: Access denied{Environment.NewLine}Metadata unreadable",
            document.Properties["Comment"]!.ToString());
    }

    [Fact]
    public void Apply_WritesCommentWhenEntryHasNoneYet()
    {
        var document = CreateDocument("broken.jpg", "/root/broken.jpg");

        ScanIssueComments.Apply([document],
            [new ScanIssue(ScanIssueSeverity.Error, "/root/broken.jpg", "Failed: CRC mismatch")]);

        Assert.Equal("Failed: CRC mismatch", document.Properties["Comment"]!.ToString());
    }

    [Fact]
    public void Apply_MatchesByUniqueFileNameWhenIssuedPathDiffersFromFullName()
    {
        var document = CreateDocument("photo.jpg", "/mounted/photo.jpg");

        var changed = ScanIssueComments.Apply([document],
            [new ScanIssue(ScanIssueSeverity.Warning, "/other/photo.jpg", "Skipped: locked")]);

        Assert.Single(changed);
        Assert.Equal("Skipped: locked", document.Properties["Comment"]!.ToString());
    }

    [Fact]
    public void Apply_IgnoresUnmatchedAmbiguousAndDuplicateIssues()
    {
        var first = CreateDocument("same.txt", "/first/same.txt");
        var second = CreateDocument("same.txt", "/second/same.txt");

        var changed = ScanIssueComments.Apply([first, second],
        [
            new ScanIssue(ScanIssueSeverity.Warning, "same.txt", "Ambiguous match"),
            new ScanIssue(ScanIssueSeverity.Warning, "/unknown/missing.txt", "Not in catalog"),
            new ScanIssue(ScanIssueSeverity.Error, null, "Pathless failure"),
            new ScanIssue(ScanIssueSeverity.Error, "/first/same.txt", "Valid issue"),
            new ScanIssue(ScanIssueSeverity.Error, "/first/same.txt", "Valid issue")
        ]);

        var updated = Assert.Single(changed);
        Assert.Same(first, updated);
        Assert.Equal("Valid issue", first.Properties["Comment"]!.ToString());
        Assert.DoesNotContain("Comment", second.Properties.Keys);
    }

    [Fact]
    public void Apply_ReturnsEmptyWhenNothingMatches()
    {
        var document = CreateDocument("keep.txt", "/root/keep.txt");

        var changed = ScanIssueComments.Apply([document],
            [new ScanIssue(ScanIssueSeverity.Error, "/root/other.txt", "Unrelated")]);

        Assert.Empty(changed);
        Assert.DoesNotContain(ScanIssueComments.PropertyKey, document.Properties.Keys);
    }

    private static CatalogDocument CreateDocument(string name, string fullName, string? comment = null)
    {
        var properties = new PropertiesCollection
        {
            ["FullName"] = fullName
        };
        if (comment is not null)
        {
            properties[ScanIssueComments.PropertyKey] = comment;
        }

        return new CatalogDocument
        {
            Id = name,
            Name = name,
            Type = CatalogDocumentType.File,
            Properties = properties
        };
    }
}
