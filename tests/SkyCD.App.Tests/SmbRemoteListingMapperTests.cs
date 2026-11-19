using System;
using System.IO;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// SMB listing projection (#626): names, sizes and UTC timestamps follow the local scan
/// conventions, and reparse points carry their resolved target as the visited identity so
/// the shared cycle logic treats them like SFTP symlinks.
/// </summary>
public sealed class SmbRemoteListingMapperTests
{
    private const string Host = "fileserver";

    private static ScanFileSystemEntry Entry(
        string name,
        bool isDirectory = false,
        long length = 0,
        DateTime? modified = null,
        string? visitedIdentity = null,
        bool isLink = false) =>
        new(
            FullPath: $@"\\{Host}\share\docs\{name}",
            Name: name,
            IsDirectory: isDirectory,
            Length: length,
            Attributes: (isDirectory ? FileAttributes.Directory : FileAttributes.Normal) | (isLink ? FileAttributes.ReparsePoint : 0),
            CreationTime: DateTime.UtcNow,
            LastAccessTime: DateTime.UtcNow,
            LastWriteTime: modified ?? DateTime.MinValue,
            IsLinkOrReparsePoint: isLink,
            VisitedIdentity: visitedIdentity ?? $@"\\{Host}\share\docs\{name}");

    [Fact]
    public void Map_CarriesNamesSizesAndDirectoryFlags()
    {
        var mapped = SmbRemoteListingMapper.Map(
            new[]
            {
                Entry("file.txt", length: 7),
                Entry("sub", isDirectory: true),
            },
            "/share/docs",
            Host);

        Assert.Equal(2, mapped.Count);

        var file = Assert.Single(mapped, entry => entry.Name == "file.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(7, file.Size);
        Assert.Equal("/share/docs/file.txt", file.FullName);
        Assert.Equal("/share/docs", file.DirectoryPath);

        var sub = Assert.Single(mapped, entry => entry.Name == "sub");
        Assert.True(sub.IsDirectory);
        Assert.Equal(0, sub.Size);
    }

    [Fact]
    public void Map_LinkEntries_CarryTheResolvedTargetAsVisitedIdentity()
    {
        var entry = Assert.Single(SmbRemoteListingMapper.Map(
            new[] { Entry("loop", isDirectory: true, isLink: true, visitedIdentity: $@"\\{Host}\share\docs") },
            "/share/docs",
            Host));

        Assert.True(entry.IsDirectory);
        Assert.Equal("/share/docs", entry.VisitedIdentity);
    }

    [Theory]
    [InlineData(@"\\other\share\target", "//other/share/target")]
    [InlineData(@"\\fileserver\share\target", "/share/target")]
    public void Map_VisitedIdentity_FollowsTheSharedPathModel(string identity, string expected)
    {
        var entry = Assert.Single(SmbRemoteListingMapper.Map(
            new[] { Entry("loop", isDirectory: true, isLink: true, visitedIdentity: identity) },
            "/share/docs",
            Host));

        Assert.Equal(expected, entry.VisitedIdentity);
    }

    [Fact]
    public void Map_NormalizesTimestampsToUtc()
    {
        var utc = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        var entry = Assert.Single(SmbRemoteListingMapper.Map(
            new[] { Entry("a.txt", length: 1, modified: utc) },
            "/share/docs",
            Host));
        Assert.Equal(utc, entry.ModifiedUtc);

        var none = Assert.Single(SmbRemoteListingMapper.Map(
            new[] { Entry("b.txt", length: 1) },
            "/share/docs",
            Host));
        Assert.Null(none.ModifiedUtc);
    }

    [Fact]
    public void Map_SkipsUnusableNames()
    {
        var mapped = SmbRemoteListingMapper.Map(
            new[] { null, Entry(""), Entry(".."), Entry("with\\slash.txt") },
            "/share/docs",
            Host);

        Assert.Empty(mapped);
    }

    [Fact]
    public void Map_PreservesBoundaryWhitespaceInFilesystemNames()
    {
        // Windows names may carry whitespace such as U+3000 at the boundary; it is part of
        // the name and must survive into the catalog.
        var entry = Assert.Single(SmbRemoteListingMapper.Map(
            new[] { Entry("\u3000Reports", length: 1) },
            "/share/docs",
            Host));

        Assert.Equal("\u3000Reports", entry.Name);
        Assert.Equal("/share/docs/\u3000Reports", entry.FullName);
    }
}
