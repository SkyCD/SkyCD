using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Sources;

/// <summary>
/// GnuDb freedb lookup: a <c>cddb query</c> to find a matching category, followed by a <c>cddb read</c>
/// that returns the disc and track fields. An empty or failed lookup yields null instead of an error.
/// </summary>
public sealed class GnudbClient : IGnuDbLookup
{
    public const string BaseUrl = "https://gnudb.gnudb.org/~cddb/cddb.cgi";

    private const string Hello = "hello=skycd+localhost+SkyCD+1.0.0&proto=6";

    private readonly HttpClient http;
    private readonly IRequestThrottle throttle;
    private readonly TimeSpan requestTimeout;

    public GnudbClient(HttpClient http, IRequestThrottle throttle, TimeSpan requestTimeout)
    {
        this.http = http;
        this.throttle = throttle;
        this.requestTimeout = requestTimeout;
    }

    public async Task<DiscMetadata?> LookupAsync(DiscToc toc, string discId, CancellationToken cancellationToken)
    {
        var match = await QueryAsync(toc, discId, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return null;
        }

        var entry = await ReadAsync(match.Value.Category, match.Value.DiscId, cancellationToken)
            .ConfigureAwait(false);
        return entry is null ? null : ParseEntry(entry);
    }

    private async Task<(string Category, string DiscId)?> QueryAsync(DiscToc toc, string discId,
        CancellationToken cancellationToken)
    {
        var playingSeconds = DiscToc.FramesToSeconds(toc.LeadOutFrames) -
                             DiscToc.FramesToSeconds(toc.TrackOffsetsFrames[0]);
        var offsets = string.Join("+", toc.TrackOffsetsFrames.Select(offset =>
            offset.ToString(CultureInfo.InvariantCulture)));
        var command = $"cddb+query+{Uri.EscapeDataString(discId)}+{toc.TrackCount}+{offsets}+" +
                      Math.Max(0, playingSeconds).ToString(CultureInfo.InvariantCulture);

        var body = await SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        var lines = SplitLines(body);
        if (lines.Count == 0 || !TryReadStatus(lines[0], out var status))
        {
            return null;
        }

        // 200 is an exact match whose category pair shares the status line; 211 lists inexact matches.
        if (status == 200)
        {
            return ParseMatchLine(lines[0].Length > 3 ? lines[0][3..] : string.Empty);
        }

        if (status != 211)
        {
            return null;
        }

        for (var index = 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line == "." || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (ParseMatchLine(line) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    private static (string Category, string DiscId)? ParseMatchLine(string line)
    {
        var parts = line.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? (parts[0], parts[1]) : null;
    }

    private async Task<string?> ReadAsync(string category, string discId, CancellationToken cancellationToken)
    {
        var command = $"cddb+read+{Uri.EscapeDataString(category)}+{Uri.EscapeDataString(discId)}";
        var body = await SendAsync(command, cancellationToken).ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        var lines = SplitLines(body);
        if (lines.Count == 0 || !TryReadStatus(lines[0], out var status) || status != 210)
        {
            return null;
        }

        return body;
    }

    private async Task<string?> SendAsync(string command, CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var timed = AudioCdHttp.BeginTimedRequest(requestTimeout, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}?cmd={command}&{Hello}");
        request.Headers.TryAddWithoutValidation("User-Agent", AudioCdHttp.UserAgent);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            timed?.Token ?? cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await AudioCdHttp.ReadBoundedStringAsync(response, timed?.Token ?? cancellationToken)
            .ConfigureAwait(false);
    }

    private static DiscMetadata? ParseEntry(string body)
    {
        var lines = SplitLines(body);
        var values = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        string? currentKey = null;

        foreach (var line in lines.Skip(1))
        {
            if (line == ".")
            {
                break;
            }

            if (line.Length > 0 && line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                currentKey = line[..separator].Trim().ToUpperInvariant();
                if (!values.TryGetValue(currentKey, out var builder))
                {
                    builder = new StringBuilder();
                    values[currentKey] = builder;
                }

                builder.Append(line[(separator + 1)..]);
            }
            else if (currentKey is not null && line.Length > 0)
            {
                values[currentKey].Append(line);
            }
        }

        if (values.Count == 0)
        {
            return null;
        }

        var discTitle = Get(values, "DTITLE");
        var (artist, album) = SplitArtistAndTitle(discTitle);
        var year = int.TryParse(Get(values, "DYEAR"), NumberStyles.None, CultureInfo.InvariantCulture,
            out var parsedYear)
            ? parsedYear
            : (int?)null;

        var tracks = new List<TrackMetadata>();
        for (var index = 0; index < 99; index++)
        {
            var key = $"TTITLE{index}";
            if (!values.TryGetValue(key, out var builder))
            {
                break;
            }

            var (trackArtist, title) = SplitArtistAndTitle(builder.ToString());
            tracks.Add(new TrackMetadata(index + 1, title, trackArtist));
        }

        return new DiscMetadata(
            Album: album,
            AlbumArtist: artist,
            Artist: artist,
            Year: year,
            Genre: NullIfBlank(Get(values, "DGENRE")),
            Country: null,
            Label: null,
            Tracks: tracks,
            MusicBrainzReleaseId: null,
            SourceKey: "gnudb");
    }

    /// <summary>freedb stores "artist / title"; a title without the separator keeps the disc artist.</summary>
    private static (string? Artist, string? Title) SplitArtistAndTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        var separator = value.IndexOf(" / ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return (null, value.Trim());
        }

        var artist = value[..separator].Trim();
        var title = value[(separator + 3)..].Trim();
        return (string.IsNullOrEmpty(artist) ? null : artist, string.IsNullOrEmpty(title) ? null : title);
    }

    private static string? Get(Dictionary<string, StringBuilder> values, string key) =>
        values.TryGetValue(key, out var builder) ? NullIfBlank(builder.ToString()) : null;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool TryReadStatus(string line, out int status)
    {
        status = 0;
        return line.Length >= 3 && int.TryParse(line.AsSpan(0, 3), NumberStyles.None, CultureInfo.InvariantCulture,
            out status);
    }

    private static List<string> SplitLines(string body) =>
        body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
}
