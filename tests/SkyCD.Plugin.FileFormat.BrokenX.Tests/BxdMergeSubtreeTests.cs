using System.Collections.Generic;
using System.Linq;
using Xunit;
using SkyCD.Plugin.FileFormat.BrokenX.Export;
using SkyCD.Plugin.FileFormat.BrokenX.Import;
using SkyCD.Plugin.FileFormat.BrokenX.Models;
using SkyCD.Plugin.FileFormat.BrokenX.Parsing;

namespace SkyCD.Plugin.FileFormat.BrokenX.Tests;

public sealed class BxdMergeSubtreeTests
{
    [Fact]
    public void Merge_DisjointSources_ConcatenatesDeterministically()
    {
        var first = ReadRows(BxdFixture.Serialize(BxdFixture.OneItemDatabase()), "MediaA");
        var second = ReadRows(BxdFixture.Serialize(BxdFixture.SiblingDatabase()), "MediaB");

        var merged = BxdMergeService.Merge([first, second]);

        Assert.Equal(first.Count + second.Count, merged.Count);
        Assert.Equal(first.Concat(second).Select(r => r["nodeId"]?.ToString()), merged.Rows.Select(r => r["nodeId"]?.ToString()));
        Assert.False(merged.Summary.IsPartialReport);
    }

    [Fact]
    public void Merge_SameMedia_UnionsRows()
    {
        var first = ReadRows(BxdFixture.Serialize(BxdFixture.OneItemDatabase()), "Media");
        var second = ReadRows(BxdFixture.Serialize(BxdFixture.SiblingDatabase()), "Media");

        var merged = BxdMergeService.Merge([first, second]);

        Assert.Single(merged.Rows, r => r["kind"]?.ToString() == "media");
        Assert.Contains(merged.Rows, r => r["name"]?.ToString() == "readme.txt");
        Assert.Contains(merged.Rows, r => r["name"]?.ToString() == "loose.txt");
    }

    [Fact]
    public void Merge_ConflictingRows_KeepsFirstAndWarns()
    {
        var first = ReadRows(BxdFixture.Serialize(BxdFixture.OneItemDatabase()));
        var conflicting = new List<Dictionary<string, object?>>(first);
        var renamed = new Dictionary<string, object?>(first.First(r => r["name"]?.ToString() == "readme.txt"), System.StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "renamed.txt"
        };
        conflicting[conflicting.FindIndex(r => r["name"]?.ToString() == "readme.txt")] = renamed;

        var merged = BxdMergeService.Merge([first, conflicting]);

        Assert.Equal(first.Count, merged.Count);
        Assert.Contains(merged.Rows, r => r["name"]?.ToString() == "readme.txt");
        Assert.Contains(merged.Summary.SkippedReasons, w => w.StartsWith("Warning:"));
        Assert.True(merged.Summary.IsPartialReport);
    }

    [Fact]
    public void SubtreeSelector_SelectsRootAndDescendants()
    {
        var rows = ReadRows(BxdFixture.Serialize(BxdFixture.SiblingDatabase()));
        var docsId = rows.First(r => r["name"]?.ToString() == "Docs")["nodeId"]?.ToString()!;

        var selected = BxdSubtreeSelector.Select(rows, [docsId]);

        Assert.Equal(3, selected.Count);
        Assert.All(selected, r => Assert.NotEqual("loose.txt", r["name"]?.ToString()));
    }

    [Fact]
    public void SubtreeSelector_UnknownId_ThrowsActionableError()
    {
        var rows = ReadRows(BxdFixture.Serialize(BxdFixture.OneItemDatabase()));
        Assert.Throws<SkyCD.Plugin.FileFormat.BrokenX.Exceptions.BxdSelectionException>(() =>
            BxdSubtreeSelector.Select(rows, ["file:Media/missing.txt"]));
    }

    [Fact]
    public void SubtreeSelector_EmptySelection_ThrowsActionableError()
    {
        var rows = ReadRows(BxdFixture.Serialize(BxdFixture.OneItemDatabase()));
        Assert.Throws<SkyCD.Plugin.FileFormat.BrokenX.Exceptions.BxdSelectionException>(() =>
            BxdSubtreeSelector.Select(rows, []));
    }

    private static IReadOnlyList<Dictionary<string, object?>> ReadRows(byte[] bytes, string fileStem = "Media")
    {
        var database = BxdReader.Read(new System.IO.MemoryStream(bytes), default);
        return BxdReportBuilder.Build(database, fileStem, Count(database)).Rows;
    }

    private static int Count(BxdDatabase database)
    {
        var count = 0;
        var stack = new Stack<BxdItem>(database.Roots);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            count++;
            foreach (var child in item.Children)
            {
                stack.Push(child);
            }
        }

        return count;
    }
}
