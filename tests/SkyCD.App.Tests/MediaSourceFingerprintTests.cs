using System;
using System.Collections.Generic;
using System.Linq;
using SkyCD.Documents;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Presentation.MediaCache;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class MediaSourceFingerprintTests
{
    private const string LastWriteTime = "2024-03-01T12:00:00.0000000+00:00";

    [Fact]
    public void Fingerprint_IsStableForTheSameSourceFacts()
    {
        var first = MediaSourceFingerprint.Create("media-root", "Music/track.mp3", 64, ParseLastWrite());
        var second = MediaSourceFingerprint.Create("media-root", "Music/track.mp3", 64, ParseLastWrite());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Fingerprint_IgnoresMachineSpecificPaths_ButTracksSourceChanges()
    {
        var catalog = BuildCatalog(@"D:\Music\Mixtape\track.mp3");
        var baseline = MediaSourceFingerprint.ForCatalogEntry(catalog.Entry, Catalog(catalog));

        var relocated = BuildCatalog(@"Z:\other\machine\track.mp3");
        Assert.Equal(baseline, MediaSourceFingerprint.ForCatalogEntry(relocated.Entry, Catalog(relocated)));

        var resized = BuildCatalog(@"D:\Music\Mixtape\track.mp3", size: 65);
        Assert.NotEqual(baseline, MediaSourceFingerprint.ForCatalogEntry(resized.Entry, Catalog(resized)));

        var rewritten = BuildCatalog(@"D:\Music\Mixtape\track.mp3", lastWriteTime: "2025-01-01T00:00:00.0000000+00:00");
        Assert.NotEqual(baseline, MediaSourceFingerprint.ForCatalogEntry(rewritten.Entry, Catalog(rewritten)));

        var moved = BuildCatalog(@"D:\Music\Mixtape\track.mp3", parentName: "Renamed Disc");
        Assert.Equal(baseline, MediaSourceFingerprint.ForCatalogEntry(moved.Entry, Catalog(moved)));
    }

    [Fact]
    public void Fingerprint_SeparatesEntriesInDifferentMediaRoots()
    {
        var first = BuildCatalog(@"D:\Music\track.mp3", rootId: "media-a");
        var second = BuildCatalog(@"D:\Music\track.mp3", rootId: "media-b");

        Assert.NotEqual(
            MediaSourceFingerprint.ForCatalogEntry(first.Entry, Catalog(first)),
            MediaSourceFingerprint.ForCatalogEntry(second.Entry, Catalog(second)));
    }

    [Fact]
    public void Fingerprint_WithoutLastWriteTime_RemainsStable()
    {
        var catalog = BuildCatalog(@"D:\Music\track.mp3", lastWriteTime: null);

        Assert.Equal(
            MediaSourceFingerprint.ForCatalogEntry(catalog.Entry, Catalog(catalog)),
            MediaSourceFingerprint.ForCatalogEntry(catalog.Entry, Catalog(catalog)));
    }

    private static DateTimeOffset? ParseLastWrite() => new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private static (CatalogDocument Root, CatalogDocument Entry) BuildCatalog(
        string fullName,
        long size = 64,
        string? lastWriteTime = LastWriteTime,
        string rootId = "media-root",
        string parentName = "Mixtape")
    {
        var root = new CatalogDocument
        {
            Id = rootId,
            Name = parentName,
            ParentId = "library",
            Type = CatalogDocumentType.Media
        };

        var properties = new PropertiesCollection { ["FullName"] = fullName };
        if (lastWriteTime is not null)
        {
            properties["LastWriteTime"] = lastWriteTime;
        }

        var entry = new CatalogDocument
        {
            Id = "file-track-1",
            Name = "track.mp3",
            ParentId = root.Id,
            Type = CatalogDocumentType.File,
            Size = size,
            Properties = properties
        };

        return (root, entry);
    }

    private static IReadOnlyList<CatalogDocument> Catalog((CatalogDocument Root, CatalogDocument Entry) catalog) =>
        [catalog.Root, catalog.Entry];
}
