using System;
using SkyCD.Presentation.Scanning;
using WebDav;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// WebDAV href resolution and collection semantics for the catalog projection (#625):
/// relative and absolute hrefs, percent-decoding, trailing-slash collections and the
/// collection's own record are handled here before the shared traversal sees the entries.
/// </summary>
public sealed class WebDavRemoteListingMapperTests
{
    private static readonly Uri RequestUri = new("http://host/pub/docs/");

    private static WebDavResource Resource(string href, bool collection = false, long? length = null, DateTime? modified = null)
    {
        var builder = new WebDavResource.Builder().WithUri(href).WithContentLength(length).WithLastModifiedDate(modified);
        return (collection ? builder.IsCollection() : builder.IsNotCollection()).Build();
    }

    [Fact]
    public void Map_ResolvesRelativeAndAbsoluteHrefs_AndDropsTheCollectionItself()
    {
        var entries = WebDavRemoteListingMapper.Map(
            new[]
            {
                Resource("/pub/docs/", collection: true),
                Resource("file.txt", length: 7),
                Resource("/pub/docs/sub/", collection: true),
                Resource("../other.txt", length: 3),
            },
            "/pub/docs",
            RequestUri);

        Assert.Equal(2, entries.Count);

        var file = Assert.Single(entries, entry => entry.Name == "file.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(7, file.Size);
        Assert.Equal("/pub/docs/file.txt", file.FullName);
        Assert.Equal("/pub/docs", file.DirectoryPath);

        var sub = Assert.Single(entries, entry => entry.Name == "sub");
        Assert.True(sub.IsDirectory);
        Assert.Equal("/pub/docs/sub", sub.FullName);
    }

    [Fact]
    public void Map_DecodesPercentEncodedNames()
    {
        var entry = Assert.Single(WebDavRemoteListingMapper.Map(
            new[] { Resource("/pub/docs/my%20file%2Bone.txt", length: 1) },
            "/pub/docs",
            RequestUri));

        Assert.Equal("my file+one.txt", entry.Name);
        Assert.Equal("/pub/docs/my file+one.txt", entry.FullName);
    }

    [Fact]
    public void Map_CollectionWithoutResourceType_UsesTheTrailingSlash()
    {
        var entry = Assert.Single(WebDavRemoteListingMapper.Map(
            new[] { Resource("sub", collection: false) },
            "/pub/docs",
            RequestUri));

        Assert.False(entry.IsDirectory);

        var slashed = Assert.Single(WebDavRemoteListingMapper.Map(
            new[] { Resource("sub/", collection: false) },
            "/pub/docs",
            RequestUri));

        Assert.True(slashed.IsDirectory);
        Assert.Equal(0, slashed.Size);
    }

    [Fact]
    public void Map_NormalizesTimestampsToUtc()
    {
        var utc = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);

        var explicitUtc = Assert.Single(WebDavRemoteListingMapper.Map(
            new[] { Resource("a.txt", length: 1, modified: utc) },
            "/pub/docs",
            RequestUri));
        Assert.Equal(utc, explicitUtc.ModifiedUtc);

        var unspecified = Assert.Single(WebDavRemoteListingMapper.Map(
            new[] { Resource("b.txt", length: 1, modified: DateTime.SpecifyKind(utc, DateTimeKind.Unspecified)) },
            "/pub/docs",
            RequestUri));
        Assert.Equal(utc, unspecified.ModifiedUtc);
    }

    [Fact]
    public void Map_SkipsUnusableRecords()
    {
        var entries = WebDavRemoteListingMapper.Map(
            new[]
            {
                null,
                Resource(string.Empty),
                Resource(".."),
                Resource("with/slash.txt"),
            },
            "/pub/docs",
            RequestUri);

        Assert.Empty(entries);
    }
}
