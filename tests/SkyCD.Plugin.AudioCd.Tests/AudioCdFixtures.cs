using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.AudioCd;
using SkyCD.Plugin.AudioCd.Sources;

namespace SkyCD.Plugin.AudioCd.Tests;

internal static class AudioCdFixtures
{
    /// <summary>
    /// The CD documented on the MusicBrainz "Disc ID Calculation" page: six tracks, offsets in absolute
    /// frames including the 150-frame lead-in, and the published disc id 49HHV7Eb8UKF3aQiNmu1GR8vKTY-.
    /// </summary>
    public static DiscToc ExampleToc() => new(1, [150, 15363, 32314, 46592, 63414, 80489], 95462);

    public static DiscMetadata MusicBrainzMetadata() => new(
        Album: "Test Album",
        AlbumArtist: "Test Artist",
        Artist: "Test Artist",
        Year: 1999,
        Genre: null,
        Country: "GB",
        Label: "Test Label",
        Tracks:
        [
            new TrackMetadata(1, "Opening", "Test Artist"),
            new TrackMetadata(2, "Second", "Test Artist"),
            new TrackMetadata(3, "Third", null),
            new TrackMetadata(4, "Fourth", null),
            new TrackMetadata(5, "Fifth", null),
            new TrackMetadata(6, "Finale", null)
        ],
        MusicBrainzReleaseId: "11111111-2222-3333-4444-555555555555",
        SourceKey: "musicbrainz");

    public static DiscMetadata GnuDbMetadata() => new(
        Album: "GnuDb Album",
        AlbumArtist: "GnuDb Artist",
        Artist: "GnuDb Artist",
        Year: 1987,
        Genre: "Rock",
        Country: null,
        Label: null,
        Tracks: [new TrackMetadata(1, "GnuDb Track", null)],
        MusicBrainzReleaseId: null,
        SourceKey: "gnudb");

    public static byte[] CoverPng(int size)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.SteelBlue);
        canvas.Flush();
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static IReadOnlyDictionary<string, string?> Settings(params (string Key, string? Value)[] pairs)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in pairs)
        {
            settings[key] = value;
        }

        return settings;
    }

    public static IReadOnlyDictionary<string, string?> SettingsFrom(AudioCdSettings settings) => settings.ToSettings();
}

internal sealed class FakeDiscTocReader : IDiscTocReader
{
    private readonly DiscToc? toc;

    public FakeDiscTocReader(DiscToc? toc)
    {
        this.toc = toc;
    }

    public int Reads { get; private set; }

    public DiscToc? Read(string rootPath, CancellationToken cancellationToken)
    {
        Reads++;
        return toc;
    }
}

internal sealed class FakeMusicBrainzLookup : IMusicBrainzLookup
{
    private readonly Func<string, CancellationToken, Task<DiscMetadata?>> handler;

    public FakeMusicBrainzLookup(DiscMetadata? result = null)
        : this((_, _) => Task.FromResult(result))
    {
    }

    public FakeMusicBrainzLookup(Func<string, CancellationToken, Task<DiscMetadata?>> handler)
    {
        this.handler = handler;
    }

    public int Calls { get; private set; }

    public string? LastDiscId { get; private set; }

    public Task<DiscMetadata?> LookupAsync(string discId, CancellationToken cancellationToken)
    {
        Calls++;
        LastDiscId = discId;
        return handler(discId, cancellationToken);
    }
}

internal sealed class FakeGnuDbLookup : IGnuDbLookup
{
    private readonly Func<DiscToc, string, CancellationToken, Task<DiscMetadata?>> handler;

    public FakeGnuDbLookup(DiscMetadata? result = null)
        : this((_, _, _) => Task.FromResult(result))
    {
    }

    public FakeGnuDbLookup(Func<DiscToc, string, CancellationToken, Task<DiscMetadata?>> handler)
    {
        this.handler = handler;
    }

    public int Calls { get; private set; }

    public Task<DiscMetadata?> LookupAsync(DiscToc toc, string discId, CancellationToken cancellationToken)
    {
        Calls++;
        return handler(toc, discId, cancellationToken);
    }
}

internal sealed class FakeCoverArtLookup : ICoverArtLookup
{
    private readonly Func<string, int, Task<CoverArtImage?>> handler;

    public FakeCoverArtLookup(byte[]? png = null)
        : this((_, _) => Task.FromResult(png is null ? null : new CoverArtImage(png, "image/png")))
    {
    }

    public FakeCoverArtLookup(Func<string, int, Task<CoverArtImage?>> handler)
    {
        this.handler = handler;
    }

    public int Calls { get; private set; }

    public int LastSize { get; private set; }

    public Task<CoverArtImage?> FetchAsync(string releaseId, int size, CancellationToken cancellationToken)
    {
        Calls++;
        LastSize = size;
        return handler(releaseId, size);
    }
}

/// <summary>An HTTP handler driven by a delegate, so each test states exactly what the source returns.</summary>
internal sealed class DelegateHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler;

    public DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        : this((request, _) => Task.FromResult(handler(request)))
    {
    }

    public DelegateHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        this.handler = handler;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return handler(request, cancellationToken);
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "text/plain")
    };

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Bytes(byte[] body, string contentType, HttpStatusCode status = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body) };
        response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return response;
    }
}
