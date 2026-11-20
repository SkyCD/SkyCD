using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.AudioCd.Exceptions;
using SkyCD.Plugin.AudioCd.Sources;

namespace SkyCD.Plugin.AudioCd;

/// <summary>
/// Reads the table of contents of a mounted audio CD and identifies it through MusicBrainz or GnuDb. The
/// capability is discovered from assembly metadata, keeps the host responsive by bounding every request, and
/// answers null for offline mode, unreadable drives and unmatched discs.
/// </summary>
public sealed class AudioCdMetadataPlugin : IAudioCdMetadataCapability
{
    private static readonly HttpClient SharedHttpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly IRequestThrottle MusicBrainzThrottle = new MinimumIntervalThrottle(TimeSpan.FromSeconds(1));
    private static readonly IRequestThrottle GnuDbThrottle = new MinimumIntervalThrottle(TimeSpan.FromSeconds(1));

    private readonly IDiscTocReader tocReader;
    private readonly Func<AudioCdSettings, AudioCdMetadataResolver> resolverFactory;

    public AudioCdMetadataPlugin()
        : this(new WindowsDiscTocReader(), CreateResolver)
    {
    }

    public AudioCdMetadataPlugin(IDiscTocReader tocReader,
        Func<AudioCdSettings, AudioCdMetadataResolver> resolverFactory)
    {
        this.tocReader = tocReader;
        this.resolverFactory = resolverFactory;
    }

    public int Priority => 0;

    public string? PrimaryExtension => AudioCdSchema.TrackExtensions[0].TrimStart('.');

    public async Task<AudioCdCapture?> CaptureAsync(AudioCdDiscRequest request,
        IProgress<MediaProgress>? progress, CancellationToken cancellationToken)
    {
        var settings = AudioCdSettings.Parse(request.Settings);

        DiscToc? toc;
        try
        {
            toc = tocReader.Read(request.RootPath, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                           !cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        if (toc is null || !toc.IsValid())
        {
            return null;
        }

        AudioCdCapture? capture;
        try
        {
            capture = await resolverFactory(settings).ResolveAsync(toc, settings, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is AudioCdTocException or AudioCdSourceException
                                               or AudioCdSettingsException)
        {
            return null;
        }

        if (capture is not null)
        {
            progress?.Report(new MediaProgress(1));
        }

        return capture;
    }

    private static AudioCdMetadataResolver CreateResolver(AudioCdSettings settings)
    {
        var timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        return new AudioCdMetadataResolver(
            new MusicBrainzClient(SharedHttpClient, MusicBrainzThrottle, timeout),
            new GnudbClient(SharedHttpClient, GnuDbThrottle, timeout),
            new CoverArtArchiveClient(SharedHttpClient, NoThrottle.Instance, timeout),
            new AudioCdCoverRenderer());
    }
}
