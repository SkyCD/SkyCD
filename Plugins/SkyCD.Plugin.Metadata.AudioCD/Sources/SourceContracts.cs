using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Sources;

/// <summary>Per-track details returned by a source; absent fields are left null rather than guessed.</summary>
public sealed record TrackMetadata(int Number, string? Title, string? Artist, double? LengthSeconds = null);

/// <summary>Disc details returned by one source, independent of how the source encoded them.</summary>
public sealed record DiscMetadata(
    string? Album,
    string? AlbumArtist,
    string? Artist,
    int? Year,
    string? Genre,
    string? Country,
    string? Label,
    IReadOnlyList<TrackMetadata> Tracks,
    string? MusicBrainzReleaseId,
    string SourceKey);

/// <summary>A downloaded cover image and its content type.</summary>
public sealed record CoverArtImage(byte[] Data, string ContentType);

/// <summary>MusicBrainz WS/2 disc lookup by MusicBrainz disc id.</summary>
public interface IMusicBrainzLookup
{
    Task<DiscMetadata?> LookupAsync(string discId, CancellationToken cancellationToken);
}

/// <summary>GnuDb freedb lookup by CDDB disc id and table of contents.</summary>
public interface IGnuDbLookup
{
    Task<DiscMetadata?> LookupAsync(DiscToc toc, string discId, CancellationToken cancellationToken);
}

/// <summary>Cover Art Archive lookup by MusicBrainz release id.</summary>
public interface ICoverArtLookup
{
    Task<CoverArtImage?> FetchAsync(string releaseId, int size, CancellationToken cancellationToken);
}

/// <summary>Serializes requests to a source that asks clients to stay below a rate.</summary>
public interface IRequestThrottle
{
    Task WaitAsync(CancellationToken cancellationToken);
}

/// <summary>No throttling, for tests and for sources without a documented limit.</summary>
public sealed class NoThrottle : IRequestThrottle
{
    public static NoThrottle Instance { get; } = new();

    public Task WaitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
