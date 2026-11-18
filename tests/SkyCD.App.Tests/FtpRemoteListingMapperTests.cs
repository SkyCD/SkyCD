using System;
using System.Collections.Generic;
using FluentFTP;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Projection of FluentFTP listing items onto the catalog entry model (#580). The remote listing
/// parsing itself belongs to FluentFTP; these tests pin the catalog-facing mapping.
/// </summary>
public sealed class FtpRemoteListingMapperTests
{

    private static readonly DateTime Stamp = new(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

    [Fact]
    public void Map_SkipsSelfAndParentDirectoryRecords()
    {
        var listing = new FtpListItem[]
        {
            new FtpListItem { Name = ".", Type = FtpObjectType.Directory, SubType = FtpObjectSubType.SelfDirectory, FullName = "/pub" },
            new FtpListItem { Name = "..", Type = FtpObjectType.Directory, SubType = FtpObjectSubType.ParentDirectory, FullName = "/" },
            new FtpListItem { Name = "docs", Type = FtpObjectType.Directory, SubType = FtpObjectSubType.SubDirectory, FullName = "/pub/docs" },
        };

        var entries = FtpRemoteListingMapper.Map(listing, "/pub");

        var entry = Assert.Single(entries);
        Assert.Equal("docs", entry.Name);
        Assert.True(entry.IsDirectory);
        Assert.Equal("/pub/docs", entry.FullName);
    }

    [Fact]
    public void Map_MapsNamesSizesPermissionsAndUtcTimestamps()
    {
        var listing = new FtpListItem[]
        {
            new FtpListItem
            {
                Name = "my file.txt",
                Type = FtpObjectType.File,
                Size = 11,
                Modified = Stamp,
                RawPermissions = "-rw-r--r--",
            },
            new FtpListItem
            {
                Name = "nested",
                Type = FtpObjectType.Directory,
                Size = -1,
                Modified = Stamp,
                RawPermissions = "drwxr-xr-x",
            },
        };

        var entries = FtpRemoteListingMapper.Map(listing, "/pub");

        var file = Assert.Single(entries, entry => entry.Name == "my file.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(11, file.Size);
        Assert.Equal("/pub/my file.txt", file.FullName);
        Assert.Equal("/pub", file.DirectoryPath);
        Assert.Equal("rw-r--r--", file.Permission);
        Assert.Equal(Stamp, file.ModifiedUtc);
        Assert.Equal(DateTimeKind.Utc, file.ModifiedUtc!.Value.Kind);

        var directory = Assert.Single(entries, entry => entry.Name == "nested");
        Assert.True(directory.IsDirectory);
        Assert.Equal(0, directory.Size);
        Assert.Equal("rwxr-xr-x", directory.Permission);
    }

    [Fact]
    public void Map_UsesTheOctalModeWhenTheServerSendsNoRawPermissions()
    {
        var listing = new FtpListItem[]
        {
            new FtpListItem { Name = "script.sh", Type = FtpObjectType.File, Size = 1, Modified = Stamp, Chmod = Convert.ToInt32("755", 8) },
        };

        var entry = Assert.Single(FtpRemoteListingMapper.Map(listing, "/"));

        Assert.Equal("rwxr-xr-x", entry.Permission);
    }

    [Fact]
    public void Map_ConvertsLocalAndUnspecifiedTimestampsToUtc()
    {
        var local = new DateTime(2024, 5, 6, 10, 8, 9, DateTimeKind.Local);
        var unspecified = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Unspecified);
        var listing = new FtpListItem[]
        {
            new FtpListItem { Name = "local.txt", Type = FtpObjectType.File, Modified = local },
            new FtpListItem { Name = "plain.txt", Type = FtpObjectType.File, Modified = unspecified },
        };

        var entries = FtpRemoteListingMapper.Map(listing, "/");

        Assert.All(entries, entry => Assert.Equal(DateTimeKind.Utc, entry.ModifiedUtc!.Value.Kind));
        Assert.Equal(local.ToUniversalTime(), entries[0].ModifiedUtc);
        Assert.Equal(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc), entries[1].ModifiedUtc);
    }

    [Fact]
    public void Map_DropsEntriesTheCatalogCannotRepresent()
    {
        var listing = new FtpListItem[]
        {
            new FtpListItem { Name = "..", Type = FtpObjectType.Directory },
            new FtpListItem { Name = "nested/name", Type = FtpObjectType.File },
            new FtpListItem { Name = "back\\slash", Type = FtpObjectType.File },
            new FtpListItem { Name = "ok.txt", Type = FtpObjectType.File },
        };

        var entries = FtpRemoteListingMapper.Map(listing, "/");

        var entry = Assert.Single(entries);
        Assert.Equal("ok.txt", entry.Name);
    }

    [Fact]
    public void Map_StripsALinkArrowFromNonLinkEntries()
    {
        var listing = new FtpListItem[]
        {
            new FtpListItem { Name = "link.txt -> target", Type = FtpObjectType.File, Size = 4 },
            new FtpListItem { Name = "shortcut", Type = FtpObjectType.Link, LinkTarget = "/etc/hosts", Size = 8 },
        };

        var entries = FtpRemoteListingMapper.Map(listing, "/pub");

        Assert.Contains(entries, entry => entry.Name == "link.txt" && entry.FullName == "/pub/link.txt");
        Assert.Contains(entries, entry => entry.Name == "shortcut" && !entry.IsDirectory && entry.Size == 0);
    }

    [Fact]
    public void Map_ResolvesTheNameFromTheFullPathWhenTheServerSendsNone()
    {
        var listing = new FtpListItem[]
        {
            new FtpListItem { Name = string.Empty, Type = FtpObjectType.File, FullName = "/pub/docs/readme.txt" },
        };

        var entry = Assert.Single(FtpRemoteListingMapper.Map(listing, "/pub/docs"));

        Assert.Equal("readme.txt", entry.Name);
        Assert.Equal("/pub/docs/readme.txt", entry.FullName);
    }

    [Fact]
    public void SymbolicMode_FormatsOctalModes()
    {
        Assert.Equal("rwxr-xr-x", FtpRemoteListingMapper.SymbolicMode("0755"));
        Assert.Equal("rw-r--r--", FtpRemoteListingMapper.SymbolicMode("644"));
        Assert.Equal("rwx------", FtpRemoteListingMapper.SymbolicMode("700"));
    }

    [Fact]
    public void Map_EmptyOrNullListingYieldsNoEntries()
    {
        Assert.Empty(FtpRemoteListingMapper.Map(null, "/pub"));
        Assert.Empty(FtpRemoteListingMapper.Map(new List<FtpListItem>(), "/pub"));
    }
}
