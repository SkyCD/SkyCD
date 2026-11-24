using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Plugin.Feature.Split.Export;
using SkyCD.Plugin.Feature.Split.Exceptions;
using SkyCD.Plugin.Feature.Split.Models;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using Xunit;

namespace SkyCD.Plugin.Feature.Split.Tests;

public class SplitPartPlanTests
{
    [Fact]
    public void Plan_RollsOverAtTheEntryLimit()
    {
        var plans = LegacyScdCatalogSplitter.Plan(Catalog(12, 20), Request(maxEntries: 5));

        Assert.Equal(3, plans.Count);
        Assert.Equal([5, 5, 2], plans.Select(plan => plan.EntryCount));
        Assert.Equal([0, 5, 10], plans.Select(plan => plan.FirstEntry));
        Assert.Equal([1, 2, 3], plans.Select(plan => plan.PartNumber));
    }

    [Fact]
    public void Plan_NamesPartsFromTheLegacyPattern()
    {
        var plans = LegacyScdCatalogSplitter.Plan(Catalog(4, 20), Request(maxEntries: 2));

        Assert.Equal(["part #1.scd", "part #2.scd"], plans.Select(plan => plan.FileName));
    }

    [Fact]
    public void Plan_KnowsThePartCountWhileNaming()
    {
        var plans = LegacyScdCatalogSplitter.Plan(Catalog(12, 20),
            Request(maxEntries: 6, pattern: "{source} {total} #N.scd"));

        Assert.Equal(["gamez 2 #1.scd", "gamez 2 #2.scd"], plans.Select(plan => plan.FileName));
    }

    [Fact]
    public void Plan_RollsOverAtTheByteLimit()
    {
        var plans = LegacyScdCatalogSplitter.Plan(Catalog(12, 20), Request(maxBytes: 63));

        Assert.Equal(4, plans.Count);
        Assert.Equal([3, 3, 3, 3], plans.Select(plan => plan.EntryCount));
        Assert.Equal([63L, 63L, 63L, 63L], plans.Select(plan => plan.Bytes));
    }

    [Fact]
    public void Plan_AppliesWhicheverLimitIsReachedFirst()
    {
        var plans = LegacyScdCatalogSplitter.Plan(Catalog(12, 20), Request(maxEntries: 2, maxBytes: 63));

        Assert.Equal([2, 2, 2, 2, 2, 2], plans.Select(plan => plan.EntryCount));
    }

    [Fact]
    public void Plan_KeepsAnEntryLargerThanTheLimitInItsOwnPart()
    {
        var catalog = new LegacyTextCatalog(
            [
                new LegacyScdEntry { Path = @"[Disk]\Huge.bin", SizeBytes = null, IsDirectory = false },
                new LegacyScdEntry { Path = @"[Disk]\Small.txt", SizeBytes = null, IsDirectory = false }
            ],
            [500, 20],
            520);

        var plans = LegacyScdCatalogSplitter.Plan(catalog, Request(maxBytes: 50));

        Assert.Equal([1, 1], plans.Select(plan => plan.EntryCount));
        Assert.Equal([503L, 23L], plans.Select(plan => plan.Bytes));
    }

    [Fact]
    public void Plan_CoversEveryEntryExactlyOnce()
    {
        var plans = LegacyScdCatalogSplitter.Plan(Catalog(17, 33), Request(maxEntries: 4, maxBytes: 140));

        Assert.Equal(17, plans.Sum(plan => plan.EntryCount));
        Assert.Equal([0, 4, 8, 12, 16], plans.Select(plan => plan.FirstEntry));
    }

    [Fact]
    public void Plan_RequiresAtLeastOneLimit()
    {
        Assert.Throws<SplitLimitException>(() => LegacyScdCatalogSplitter.Plan(Catalog(3, 20), Request()));
        Assert.Throws<SplitLimitException>(() =>
            LegacyScdCatalogSplitter.Plan(Catalog(3, 20), Request(maxEntries: -1, maxBytes: 10)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("catalog.scd")]
    [InlineData(@"parts\part #N.scd")]
    [InlineData("../part #N.scd")]
    [InlineData("c:/temp/part #N.scd")]
    [InlineData("part #N")]
    public void Plan_RejectsPatternsThatDoNotNameOnePartPerFile(string pattern)
    {
        Assert.Throws<SplitPatternException>(() =>
            LegacyScdCatalogSplitter.Plan(Catalog(2, 20), Request(maxEntries: 1, pattern: pattern)));
    }

    private static LegacyTextCatalog Catalog(int entries, int lineBytes) => new(
        [.. Enumerable.Range(0, entries).Select(index => new LegacyScdEntry
        {
            Path = $@"[Disk]\File{index}.txt",
            SizeBytes = 10,
            IsDirectory = false
        })],
        [.. Enumerable.Repeat(lineBytes, entries)],
        (long)entries * lineBytes);

    private static SplitRequest Request(int maxEntries = 0, long maxBytes = 0, string pattern = "part #N.scd") => new()
    {
        SourcePath = @"C:\catalogs\gamez.scd",
        OutputFolder = @"C:\out",
        FileNamePattern = pattern,
        MaxEntriesPerPart = maxEntries,
        MaxPartSizeBytes = maxBytes
    };
}
