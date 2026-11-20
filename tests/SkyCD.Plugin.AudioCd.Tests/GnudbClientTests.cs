using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.AudioCd.Sources;
using Xunit;

namespace SkyCD.Plugin.AudioCd.Tests;

public sealed class GnudbClientTests
{
    private const string EntryBody = """
        210 rock 49hhv7eb
        # Record shipped with the CD
        DTITLE=Test Artist / Test Album
        DYEAR=1999
        DGENRE=Alternative
        TTITLE0=Opening
        TTITLE1=Test Artist / Second
        EXTRA=ignored
        .
        """;

    [Fact]
    public async Task MatchedDisc_ReadsCategoryAndParsesTheEntry()
    {
        var handler = new DelegateHttpMessageHandler(request =>
            request.RequestUri!.Query.Contains("cmd=cddb+query", StringComparison.Ordinal)
                ? DelegateHttpMessageHandler.Text("200 rock 49hhv7eb\n")
                : DelegateHttpMessageHandler.Text(EntryBody));

        var disc = await CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "49hhv7eb",
            CancellationToken.None);

        Assert.NotNull(disc);
        Assert.Equal("Test Album", disc.Album);
        Assert.Equal("Test Artist", disc.Artist);
        Assert.Equal(1999, disc.Year);
        Assert.Equal("Alternative", disc.Genre);
        Assert.Equal("gnudb", disc.SourceKey);
        Assert.Null(disc.MusicBrainzReleaseId);
        Assert.Equal(2, disc.Tracks.Count);
        Assert.Equal("Opening", disc.Tracks[0].Title);
        Assert.Null(disc.Tracks[0].Artist);
        Assert.Equal("Test Artist", disc.Tracks[1].Artist);
        Assert.Equal("Second", disc.Tracks[1].Title);
    }

    [Fact]
    public async Task Query_CarriesDiscIdTrackOffsetsAndPlayingTime()
    {
        string? query = null;
        var handler = new DelegateHttpMessageHandler(request =>
        {
            query ??= request.RequestUri!.Query;
            return request.RequestUri!.Query.Contains("cmd=cddb+query", StringComparison.Ordinal)
                ? DelegateHttpMessageHandler.Text("202 No match found.\n")
                : DelegateHttpMessageHandler.Text(EntryBody);
        });

        await CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "49hhv7eb", CancellationToken.None);

        Assert.NotNull(query);
        Assert.Contains("cddb+query+49hhv7eb+6", query);
        Assert.Contains("+150+15363+32314+46592+63414+80489", query);
        Assert.Contains("+1270", query);
    }

    [Fact]
    public async Task NoMatch_ReturnsNullAfterASingleRequest()
    {
        var handler = new DelegateHttpMessageHandler(_ =>
            DelegateHttpMessageHandler.Text("202 No match found.\n"));

        var disc = await CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "missing",
            CancellationToken.None);

        Assert.Null(disc);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task MultipleMatches_ReadTheFirstOne()
    {
        var handler = new DelegateHttpMessageHandler(request =>
            request.RequestUri!.Query.Contains("cmd=cddb+query", StringComparison.Ordinal)
                ? DelegateHttpMessageHandler.Text(
                    "211 Found inexact matches\nrock 11111111 Speaker / One\nmisc 22222222 Speaker / Two\n.\n")
                : DelegateHttpMessageHandler.Text(EntryBody));

        var disc = await CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "abc",
            CancellationToken.None);

        Assert.NotNull(disc);
        Assert.Contains("cmd=cddb+read+rock+11111111", handler.Requests[^1].RequestUri!.Query);
    }

    [Fact]
    public async Task FailedRead_ReturnsNull()
    {
        var handler = new DelegateHttpMessageHandler(request =>
            request.RequestUri!.Query.Contains("cmd=cddb+query", StringComparison.Ordinal)
                ? DelegateHttpMessageHandler.Text("200 rock 49hhv7eb\n")
                : DelegateHttpMessageHandler.Text("500 read failed\n", HttpStatusCode.InternalServerError));

        Assert.Null(await CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "49hhv7eb",
            CancellationToken.None));
    }

    [Fact]
    public async Task LongValues_AreJoinedFromContinuationLines()
    {
        var body = """
            210 rock 49hhv7eb
            DTITLE=Test Artist / A Very Long
            Album Title
            TTITLE0=First Half
             Second Half
            .
            """;
        var handler = new DelegateHttpMessageHandler(request =>
            request.RequestUri!.Query.Contains("cmd=cddb+query", StringComparison.Ordinal)
                ? DelegateHttpMessageHandler.Text("200 rock 49hhv7eb\n")
                : DelegateHttpMessageHandler.Text(body));

        var disc = await CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "49hhv7eb",
            CancellationToken.None);

        Assert.NotNull(disc);
        Assert.Equal("A Very LongAlbum Title", disc.Album);
        Assert.Equal("First Half Second Half", disc.Tracks[0].Title);
    }

    [Fact]
    public async Task UnreachableSource_SurfacesAsAnHttpFailureForTheResolverToAbsorb()
    {
        var handler = new DelegateHttpMessageHandler(_ =>
            throw new HttpRequestException("network down"));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            CreateClient(handler).LookupAsync(AudioCdFixtures.ExampleToc(), "49hhv7eb", CancellationToken.None));
    }

    private static GnudbClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NoThrottle.Instance, TimeSpan.FromSeconds(5));
}

