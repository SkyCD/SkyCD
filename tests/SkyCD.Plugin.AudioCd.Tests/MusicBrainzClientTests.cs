using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.AudioCd.Exceptions;
using SkyCD.Plugin.AudioCd.Sources;
using Xunit;

namespace SkyCD.Plugin.AudioCd.Tests;

public sealed class MusicBrainzClientTests
{
    private const string MatchedBody = """
        {
          "disc": {
            "id": "49HHV7Eb8UKF3aQiNmu1GR8vKTY-",
            "releases": [
              {
                "id": "11111111-2222-3333-4444-555555555555",
                "title": "Test Album",
                "date": "1999-04-01",
                "country": "GB",
                "artist-credit": [{ "name": "Test Artist", "joinphrase": "" }],
                "label-info": [{ "label": { "name": "Test Label" } }],
                "media": [
                  {
                    "position": 1,
                    "tracks": [
                      {
                        "position": 1,
                        "title": "Opening",
                        "length": 202840,
                        "artist-credit": [{ "name": "Test Artist", "joinphrase": "" }]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

    [Fact]
    public async Task MatchedDisc_ParsesReleaseAndTracks()
    {
        var handler = new DelegateHttpMessageHandler(_ => DelegateHttpMessageHandler.Json(MatchedBody));
        var client = CreateClient(handler);

        var disc = await client.LookupAsync("49HHV7Eb8UKF3aQiNmu1GR8vKTY-", CancellationToken.None);

        Assert.NotNull(disc);
        Assert.Equal("Test Album", disc.Album);
        Assert.Equal("Test Artist", disc.Artist);
        Assert.Equal("Test Artist", disc.AlbumArtist);
        Assert.Equal(1999, disc.Year);
        Assert.Equal("GB", disc.Country);
        Assert.Equal("Test Label", disc.Label);
        Assert.Equal("musicbrainz", disc.SourceKey);
        Assert.Equal("11111111-2222-3333-4444-555555555555", disc.MusicBrainzReleaseId);
        var track = Assert.Single(disc.Tracks);
        Assert.Equal(1, track.Number);
        Assert.Equal("Opening", track.Title);
        Assert.Equal("Test Artist", track.Artist);
        Assert.Equal(202.84, track.LengthSeconds);
    }

    [Fact]
    public async Task Request_CarriesUserAgentAndDiscIdQuery()
    {
        HttpRequestMessage? captured = null;
        var handler = new DelegateHttpMessageHandler(request =>
        {
            captured = request;
            return DelegateHttpMessageHandler.Json(MatchedBody);
        });

        await CreateClient(handler).LookupAsync("abcDEF123-", CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Contains("discid/abcDEF123-", captured.RequestUri!.AbsoluteUri);
        Assert.Contains("fmt=json", captured.RequestUri.Query);
        Assert.Contains(captured.Headers.GetValues("User-Agent"),
            value => value.Contains("SkyCD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingDisc_ReturnsNull()
    {
        var handler = new DelegateHttpMessageHandler(_ =>
            DelegateHttpMessageHandler.Json("{\"error\":\"Not Found\"}", HttpStatusCode.NotFound));

        Assert.Null(await CreateClient(handler).LookupAsync("unknown", CancellationToken.None));
    }

    [Fact]
    public async Task MalformedJson_IsReportedAsASourceException()
    {
        var handler = new DelegateHttpMessageHandler(_ => DelegateHttpMessageHandler.Json("{not json"));

        await Assert.ThrowsAsync<AudioCdSourceException>(() =>
            CreateClient(handler).LookupAsync("broken", CancellationToken.None));
    }

    [Fact]
    public async Task SlowSource_TimesOutInsteadOfHanging()
    {
        var handler = new DelegateHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return DelegateHttpMessageHandler.Json(MatchedBody);
        });
        var client = new MusicBrainzClient(new HttpClient(handler), NoThrottle.Instance,
            TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.LookupAsync("slow", CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var handler = new DelegateHttpMessageHandler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(DelegateHttpMessageHandler.Json(MatchedBody));
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateClient(handler).LookupAsync("cancelled", cancelled.Token));
    }

    [Fact]
    public async Task Throttle_SpacesConsecutiveRequests()
    {
        var handler = new DelegateHttpMessageHandler(_ => DelegateHttpMessageHandler.Json(MatchedBody));
        using var throttle = new MinimumIntervalThrottle(TimeSpan.FromMilliseconds(80));
        var client = new MusicBrainzClient(new HttpClient(handler), throttle, TimeSpan.FromSeconds(5));
        var stopwatch = Stopwatch.StartNew();

        await client.LookupAsync("first", CancellationToken.None);
        var firstElapsed = stopwatch.Elapsed;
        await client.LookupAsync("second", CancellationToken.None);

        Assert.True(stopwatch.Elapsed - firstElapsed >= TimeSpan.FromMilliseconds(60),
            $"Second request followed the first after {stopwatch.Elapsed - firstElapsed}.");
    }

    private static MusicBrainzClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), NoThrottle.Instance, TimeSpan.FromSeconds(5));
}
