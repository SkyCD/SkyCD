using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet.Sftp;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class SftpRemoteListingMapperTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    public void MapItem_DropsNamesTheCatalogCannotRepresent(string name)
    {
        var file = new FakeSftpFile(name, "/dir/" + name.Trim());
        Assert.Null(SftpRemoteListingMapper.MapItem(file, "/dir"));
    }

    [Fact]
    public void MapItem_ProjectsFilesWithSizePermissionAndUtcTimestamp()
    {
        var modified = new DateTime(2024, 5, 1, 12, 30, 0, DateTimeKind.Utc);
        var file = new FakeSftpFile("file.txt", "/dir/file.txt", length: 42, modifiedUtc: modified)
        {
            OwnerCanRead = true,
            OwnerCanWrite = true,
            GroupCanRead = true,
            OthersCanRead = true,
        };

        var entry = SftpRemoteListingMapper.MapItem(file, "/dir");

        Assert.NotNull(entry);
        Assert.Equal("file.txt", entry.Name);
        Assert.Equal("/dir/file.txt", entry.FullName);
        Assert.Equal("/dir", entry.DirectoryPath);
        Assert.False(entry.IsDirectory);
        Assert.Equal(42, entry.Size);
        Assert.Equal("rw-r--r--", entry.Permission);
        Assert.Equal(modified, entry.ModifiedUtc);
        Assert.Equal("/dir/file.txt", entry.VisitedIdentity);
    }

    [Fact]
    public void MapItem_ProjectsDirectoriesWithZeroSize()
    {
        var file = new FakeSftpFile("child", "/dir/child", isDirectory: true, length: 4096);

        var entry = SftpRemoteListingMapper.MapItem(file, "/dir");

        Assert.NotNull(entry);
        Assert.True(entry.IsDirectory);
        Assert.Equal(0, entry.Size);
    }

    [Fact]
    public void MapItem_NormalizesTheVisitedIdentityThroughTheListingBase()
    {
        // SSH.NET builds entries from the canonical base of the listed directory; the catalog
        // keeps the requested path while the identity follows the canonical one.
        var file = new FakeSftpFile("x", "/a/x");

        var entry = SftpRemoteListingMapper.MapItem(file, "/link");

        Assert.NotNull(entry);
        Assert.Equal("/link/x", entry.FullName);
        Assert.Equal("/a/x", entry.VisitedIdentity);
    }

    [Fact]
    public void MapItem_ReportsNullTimestampWhenServerDidNotReportOne()
    {
        var file = new FakeSftpFile("file.txt", "/dir/file.txt", modifiedUtc: DateTime.MinValue);

        var entry = SftpRemoteListingMapper.MapItem(file, "/dir");

        Assert.NotNull(entry);
        Assert.Null(entry.ModifiedUtc);
    }

    [Fact]
    public void MapItem_ResolvesSymlinkedDirectoryToItsTargetIdentity()
    {
        var link = new FakeSftpFile("loop", "/a/loop", isSymbolicLink: true);
        var target = new FakeSftpFile("a", "/a", isDirectory: true);

        var entry = SftpRemoteListingMapper.MapItem(link, "/a", _ => target);

        Assert.NotNull(entry);
        Assert.True(entry.IsDirectory);
        Assert.Equal("/a/loop", entry.FullName);
        Assert.Equal("/a", entry.VisitedIdentity);
        Assert.Equal(0, entry.Size);
    }

    [Fact]
    public void MapItem_DanglingSymlinkStaysAFileEntry()
    {
        var link = new FakeSftpFile("ghost", "/a/ghost", isSymbolicLink: true, length: 12);

        var entry = SftpRemoteListingMapper.MapItem(link, "/a", _ => null);

        Assert.NotNull(entry);
        Assert.False(entry.IsDirectory);
        Assert.Equal(0, entry.Size);
        Assert.Equal("/a/ghost", entry.VisitedIdentity);
    }

    [Fact]
    public void Map_CollectsProjectedEntriesAndSkipsNulls()
    {
        var listing = new ISftpFile?[]
        {
            null,
            new FakeSftpFile(".", "/dir/."),
            new FakeSftpFile("file.txt", "/dir/file.txt", length: 1),
        };

        var entries = SftpRemoteListingMapper.Map(listing, "/dir");

        var entry = Assert.Single(entries);
        Assert.Equal("file.txt", entry.Name);
    }

    private sealed class FakeSftpFile : ISftpFile
    {
        public FakeSftpFile(
            string name,
            string fullName,
            bool isDirectory = false,
            long length = 0,
            DateTime? modifiedUtc = null,
            bool isSymbolicLink = false)
        {
            Name = name;
            FullName = fullName;
            IsDirectory = isDirectory;
            IsSymbolicLink = isSymbolicLink;
            IsRegularFile = !isDirectory && !isSymbolicLink;
            Length = length;
            LastWriteTimeUtc = modifiedUtc ?? DateTime.MinValue;
        }

        public SftpFileAttributes Attributes => null!;

        public string FullName { get; }

        public string Name { get; }

        public DateTime LastAccessTime { get; set; }

        public DateTime LastWriteTime { get; set; }

        public DateTime LastAccessTimeUtc { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }

        public long Length { get; }

        public int UserId { get; set; }

        public int GroupId { get; set; }

        public bool IsSocket => false;

        public bool IsSymbolicLink { get; }

        public bool IsRegularFile { get; }

        public bool IsBlockDevice => false;

        public bool IsDirectory { get; }

        public bool IsCharacterDevice => false;

        public bool IsNamedPipe => false;

        public bool OwnerCanRead { get; set; }

        public bool OwnerCanWrite { get; set; }

        public bool OwnerCanExecute { get; set; }

        public bool GroupCanRead { get; set; }

        public bool GroupCanWrite { get; set; }

        public bool GroupCanExecute { get; set; }

        public bool OthersCanRead { get; set; }

        public bool OthersCanWrite { get; set; }

        public bool OthersCanExecute { get; set; }

        public void SetPermissions(short mode) => throw new NotSupportedException();

        public void Delete() => throw new NotSupportedException();

        public Task DeleteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public void MoveTo(string newPath) => throw new NotSupportedException();

        public void UpdateStatus() => throw new NotSupportedException();
    }
}
