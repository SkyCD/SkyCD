using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Presentation.UndoRedo;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Covers the pin registry that keeps media cache artifacts alive while undo history can still restore the
/// entry they belong to.
/// </summary>
public sealed class CatalogHistoryCachePinsTests
{
    [Fact]
    public void Pin_MakesEntriesSurviveTheOrphanCheck()
    {
        var pins = new CatalogHistoryCachePins();

        pins.Pin("action-1", new[] { "entry-a", "entry-b" });

        Assert.True(pins.IsPinned("entry-a"));
        Assert.True(pins.IsPinned("entry-b"));
        Assert.False(pins.IsPinned("entry-c"));
        Assert.Equal(2, pins.PinnedEntryCount);
    }

    [Fact]
    public void OverlappingOwners_KeepAnEntryPinnedUntilTheLastOneReleases()
    {
        var pins = new CatalogHistoryCachePins();
        var released = new List<string>();
        pins.Released += (_, args) => released.AddRange(args.EntryIds);

        // Two deletions of overlapping subtrees both hold the shared descendant.
        pins.Pin("action-1", new[] { "shared", "only-one" });
        pins.Pin("action-2", new[] { "shared", "only-two" });

        pins.Release("action-1");

        Assert.True(pins.IsPinned("shared"), "A second owner still needs the shared entry.");
        Assert.False(pins.IsPinned("only-one"));
        Assert.Equal(new[] { "only-one" }, released);

        pins.Release("action-2");

        Assert.False(pins.IsPinned("shared"));
        Assert.Equal(0, pins.PinnedEntryCount);

        // Release order follows the owner list, not the order entries were pinned, so compare as a set.
        Assert.Equal(
            new[] { "only-one", "only-two", "shared" },
            released.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Release_RaisesNothingWhenNoPinCountReachedZero()
    {
        var pins = new CatalogHistoryCachePins();
        var raised = 0;
        pins.Released += (_, _) => raised++;

        pins.Pin("action-1", new[] { "entry-a" });
        pins.Pin("action-2", new[] { "entry-a" });

        pins.Release("action-1");

        Assert.Equal(0, raised);
    }

    [Fact]
    public void DuplicateIdsInOnePin_AreCountedOnce()
    {
        var pins = new CatalogHistoryCachePins();

        pins.Pin("action-1", new[] { "entry-a", "entry-a", " ", "" });

        Assert.Equal(1, pins.PinnedEntryCount);

        pins.Release("action-1");
        Assert.False(pins.IsPinned("entry-a"));
    }

    [Fact]
    public void ReleasingAnUnknownOwner_ChangesNothing()
    {
        var pins = new CatalogHistoryCachePins();
        pins.Pin("action-1", new[] { "entry-a" });

        pins.Release("action-does-not-exist");

        Assert.True(pins.IsPinned("entry-a"));
    }

    [Fact]
    public void Clear_ReleasesEverythingAndReportsIt()
    {
        var pins = new CatalogHistoryCachePins();
        IReadOnlyCollection<string>? released = null;
        pins.Released += (_, args) => released = args.EntryIds;

        pins.Pin("action-1", new[] { "entry-a" });
        pins.Pin("action-2", new[] { "entry-b" });

        pins.Clear();

        Assert.Equal(0, pins.PinnedEntryCount);
        Assert.Equal(new[] { "entry-a", "entry-b" }, released!.OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void Clear_OnAnEmptyRegistry_RaisesNothing()
    {
        var pins = new CatalogHistoryCachePins();
        var raised = 0;
        pins.Released += (_, _) => raised++;

        pins.Clear();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void PinnedEntryIds_IsASnapshotAndCanBeEnumeratedSafely()
    {
        var pins = new CatalogHistoryCachePins();
        pins.Pin("action-1", new[] { "entry-a", "entry-b" });

        var snapshot = pins.PinnedEntryIds;
        pins.Release("action-1");

        Assert.Equal(2, snapshot.Count);
        Assert.Equal(0, pins.PinnedEntryCount);
    }

    [Fact]
    public void BlankArguments_AreRejected()
    {
        var pins = new CatalogHistoryCachePins();

        Assert.Throws<ArgumentException>(() => pins.Pin(" ", new[] { "entry-a" }));
        Assert.Throws<ArgumentNullException>(() => pins.Pin("action-1", null!));
        Assert.Throws<ArgumentException>(() => pins.Release(string.Empty));
        Assert.False(pins.IsPinned(string.Empty));
    }
}
