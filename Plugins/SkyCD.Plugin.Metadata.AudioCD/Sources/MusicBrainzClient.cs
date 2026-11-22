using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.AudioCD.Exceptions;

namespace SkyCD.Plugin.Metadata.AudioCD.Sources;

/// <summary>
/// MusicBrainz WS/2 disc lookup. Sends a descriptive User-Agent and one request per second, and treats a
/// missing disc as "no metadata" rather than an error.
/// </summary>
public sealed class MusicBrainzClient : IMusicBrainzLookup
{
    public const string BaseUrl = "https://musicbrainz.org/ws/2/discid/";

    private readonly HttpClient http;
    private readonly IRequestThrottle throttle;
    private readonly TimeSpan requestTimeout;

    public MusicBrainzClient(HttpClient http, IRequestThrottle throttle, TimeSpan requestTimeout)
    {
        this.http = http;
        this.throttle = throttle;
        this.requestTimeout = requestTimeout;
    }

    public async Task<DiscMetadata?> LookupAsync(string discId, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}{Uri.EscapeDataString(discId)}?fmt=json&inc=artists%2Brecordings";

        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);

        using var timed = AudioCdHttp.BeginTimedRequest(requestTimeout, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", AudioCdHttp.UserAgent);

        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            timed?.Token ?? cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await AudioCdHttp.ReadBoundedStringAsync(response, timed?.Token ?? cancellationToken)
            .ConfigureAwait(false);
        if (body is null)
        {
            return null;
        }

        return Parse(discId, body);
    }

    private static DiscMetadata? Parse(string discId, string body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new AudioCdSourceException("MusicBrainz returned a response that was not valid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("disc", out var disc) || !disc.TryGetProperty("releases", out var releases) ||
                releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
            {
                return null;
            }

            var release = releases[0];
            var albumArtist = JoinArtistCredit(release);
            var year = ParseYear(ReadString(release, "date"));
            var country = ReadString(release, "country");
            var label = ReadLabel(release);
            var releaseId = ReadString(release, "id");
            var tracks = ReadTracks(release, discId);

            return new DiscMetadata(
                Album: ReadString(release, "title"),
                AlbumArtist: albumArtist,
                Artist: albumArtist,
                Year: year,
                Genre: null,
                Country: country,
                Label: label,
                Tracks: tracks,
                MusicBrainzReleaseId: releaseId,
                SourceKey: "musicbrainz");
        }
    }

    private static IReadOnlyList<TrackMetadata> ReadTracks(JsonElement release, string discId)
    {
        if (!release.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<TrackMetadata>();
        }

        var tracks = new List<TrackMetadata>();
        foreach (var medium in media.EnumerateArray())
        {
            if (!medium.TryGetProperty("tracks", out var mediumTracks) ||
                mediumTracks.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var track in mediumTracks.EnumerateArray())
            {
                var number = ReadInt(track, "position") ?? tracks.Count + 1;
                var recording = track.TryGetProperty("recording", out var recordingElement) &&
                                recordingElement.ValueKind == JsonValueKind.Object
                    ? recordingElement
                    : track;
                var title = ReadString(recording, "title");
                var artist = JoinArtistCredit(recording);
                var lengthMs = ReadInt(recording, "length") ?? ReadInt(track, "length");
                var lengthSeconds = lengthMs is null ? (double?)null : Math.Round(lengthMs.Value / 1000d, 3);
                tracks.Add(new TrackMetadata(number, title, artist, lengthSeconds));
            }
        }

        return tracks;
    }

    private static string? JoinArtistCredit(JsonElement element)
    {
        if (!element.TryGetProperty("artist-credit", out var credits) ||
            credits.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var credit in credits.EnumerateArray())
        {
            if (credit.ValueKind == JsonValueKind.String)
            {
                parts.Add(credit.GetString() ?? string.Empty);
                continue;
            }

            var name = ReadString(credit, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                parts.Add(name);
            }

            var joinPhrase = ReadString(credit, "joinphrase");
            if (!string.IsNullOrEmpty(joinPhrase))
            {
                parts.Add(joinPhrase);
            }
        }

        var joined = string.Concat(parts).Trim();
        return string.IsNullOrWhiteSpace(joined) ? null : joined;
    }

    private static string? ReadLabel(JsonElement release)
    {
        if (!release.TryGetProperty("label-info", out var labels) || labels.ValueKind != JsonValueKind.Array ||
            labels.GetArrayLength() == 0)
        {
            return null;
        }

        var first = labels[0];
        if (!first.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ReadString(label, "name");
    }

    private static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date.Length < 4)
        {
            return null;
        }

        return int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }
}
