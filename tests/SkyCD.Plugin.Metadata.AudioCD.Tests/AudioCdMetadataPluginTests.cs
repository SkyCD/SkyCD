using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.AudioCD;
using SkyCD.Plugin.Metadata.AudioCD.Sources;
using SkyCD.Plugin.Runtime.Factories;
using Xunit;
using SkyCD.Plugin.Metadata.AudioCD.Export;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Tests;

public sealed class AudioCdMetadataPluginTests
{
    [Fact]
    public async Task MatchedDisc_ProducesDiscAndTrackValues()
    {
        var capture = await CaptureAsync(AudioCdSettings.Default, mb: AudioCdFixtures.MusicBrainzMetadata());

        Assert.NotNull(capture);
        Assert.Equal("skycd.audio-cd.disc", capture.DiscSchema.Id);
        Assert.Equal("Test Album", Text(capture.DiscValues, AudioCdSchema.Album));
        Assert.Equal("Test Artist", Text(capture.DiscValues, AudioCdSchema.Artist));
        Assert.Equal("Test Artist", Text(capture.DiscValues, AudioCdSchema.AlbumArtist));
        Assert.Equal(1999L, Integer(capture.DiscValues, AudioCdSchema.Year));
        Assert.Equal(6L, Integer(capture.DiscValues, AudioCdSchema.TrackCount));
        Assert.Equal("49HHV7Eb8UKF3aQiNmu1GR8vKTY-", Text(capture.DiscValues, AudioCdSchema.MusicBrainzDiscId));
        Assert.Equal("musicbrainz", Text(capture.DiscValues, AudioCdSchema.Source));

        Assert.Equal(6, capture.Tracks.Count);
        Assert.Equal("Opening", Text(capture.Tracks[0].Values, AudioCdSchema.Title));
        Assert.Equal(1L, Integer(capture.Tracks[0].Values, AudioCdSchema.TrackNumber));
        Assert.Equal(Math.Round((15363 - 150) / 75d, 3), Number(capture.Tracks[0].Values, AudioCdSchema.Length));
        Assert.Equal("Test Album", Text(capture.Tracks[0].Values, AudioCdSchema.Album));
    }

    [Fact]
    public async Task DiscAndTrackArtistTags_AreGatedIndependently()
    {
        var withoutDiscArtist = await CaptureAsync(
            AudioCdSettings.Parse(AudioCdFixtures.Settings((AudioCdSettings.DiscArtistTagsKey, "false"))),
            mb: AudioCdFixtures.MusicBrainzMetadata());
        var withoutTrackArtist = await CaptureAsync(
            AudioCdSettings.Parse(AudioCdFixtures.Settings((AudioCdSettings.TrackArtistTagsKey, "false"))),
            mb: AudioCdFixtures.MusicBrainzMetadata());

        Assert.Null(Find(withoutDiscArtist!.DiscValues, AudioCdSchema.Artist));
        Assert.Null(Find(withoutDiscArtist.DiscValues, AudioCdSchema.AlbumArtist));
        Assert.Equal("Test Artist", Text(withoutDiscArtist.Tracks[0].Values, AudioCdSchema.Artist));

        Assert.NotNull(Find(withoutTrackArtist!.DiscValues, AudioCdSchema.Artist));
        Assert.Null(Find(withoutTrackArtist.Tracks[0].Values, AudioCdSchema.Artist));
    }

    [Fact]
    public async Task Covers_AreIndependentlyEnabledAndSized()
    {
        var mb = AudioCdFixtures.MusicBrainzMetadata();
        var capture = await CaptureAsync(AudioCdSettings.Parse(AudioCdFixtures.Settings(
            (AudioCdSettings.DiscCoverSizeKey, "100"),
            (AudioCdSettings.TrackCoverSizeKey, "50"))), mb: mb);

        Assert.NotNull(capture!.DiscCover);
        Assert.All(capture.Tracks, track => Assert.NotNull(track.Cover));
        Assert.Equal(100, LongestEdge(capture.DiscCover!.Data.ToArray()));
        Assert.Equal(50, LongestEdge(capture.Tracks[0].Cover!.Data.ToArray()));
    }

    [Fact]
    public async Task DisabledCovers_SkipTheDownload()
    {
        var cover = new FakeCoverArtLookup(AudioCdFixtures.CoverPng(300));
        var capture = await CaptureAsync(AudioCdSettings.Parse(AudioCdFixtures.Settings(
            (AudioCdSettings.DiscCoversKey, "false"),
            (AudioCdSettings.TrackCoversKey, "false"))), mb: AudioCdFixtures.MusicBrainzMetadata(), cover: cover);

        Assert.NotNull(capture);
        Assert.Null(capture.DiscCover);
        Assert.All(capture.Tracks, track => Assert.Null(track.Cover));
        Assert.Equal(0, cover.Calls);
    }

    [Fact]
    public async Task MissingArtwork_StillImportsTheDisc()
    {
        var capture = await CaptureAsync(AudioCdSettings.Default, mb: AudioCdFixtures.MusicBrainzMetadata(),
            cover: new FakeCoverArtLookup());

        Assert.NotNull(capture);
        Assert.Null(capture.DiscCover);
    }

    [Fact]
    public async Task OfflineMode_IdentifiesNothingAndMakesNoRequests()
    {
        var mb = new FakeMusicBrainzLookup(AudioCdFixtures.MusicBrainzMetadata());
        var capture = await CaptureAsync(
            AudioCdSettings.Parse(AudioCdFixtures.Settings((AudioCdSettings.OfflineKey, "true"))),
            musicBrainz: mb, metadata: AudioCdFixtures.MusicBrainzMetadata());

        Assert.Null(capture);
        Assert.Equal(0, mb.Calls);
    }

    [Fact]
    public async Task UnmatchedDisc_ReturnsNull()
    {
        Assert.Null(await CaptureAsync(AudioCdSettings.Default, mb: null, gnu: null));
    }

    [Fact]
    public async Task UnreachableSource_FallsThroughToTheNextSource()
    {
        var mb = new FakeMusicBrainzLookup((_, _) => throw new HttpRequestExceptionLike());
        var gnu = new FakeGnuDbLookup(AudioCdFixtures.GnuDbMetadata());

        var capture = await CaptureAsync(AudioCdSettings.Default, mb: null, gnu: AudioCdFixtures.GnuDbMetadata(),
            musicBrainz: mb, gnuDb: gnu);

        Assert.NotNull(capture);
        Assert.Equal("gnudb", Text(capture.DiscValues, AudioCdSchema.Source));
        Assert.Equal(1, mb.Calls);
        Assert.Equal(1, gnu.Calls);
    }

    [Fact]
    public async Task InvalidToc_ReturnsNull()
    {
        var plugin = CreatePlugin(new FakeDiscTocReader(new DiscToc(1, [], 0)),
            _ => new AudioCdMetadataResolver(new FakeMusicBrainzLookup(), new FakeGnuDbLookup(),
                new FakeCoverArtLookup(), new AudioCdCoverRenderer()));

        var capture = await plugin.CaptureAsync(
            new AudioCdDiscRequest("E:\\", new Dictionary<string, string?>()), null, CancellationToken.None);

        Assert.Null(capture);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var mb = new FakeMusicBrainzLookup((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<DiscMetadata?>(null);
        });
        var plugin = CreatePlugin(new FakeDiscTocReader(AudioCdFixtures.ExampleToc()),
            _ => new AudioCdMetadataResolver(mb, new FakeGnuDbLookup(), new FakeCoverArtLookup(),
                new AudioCdCoverRenderer()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.CaptureAsync(
            new AudioCdDiscRequest("E:\\", new Dictionary<string, string?>()), null, cancelled.Token));
    }

    [Fact]
    public void AssemblyDiscovery_FindsMetadataAndConfigurationCapabilities()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(AudioCdMetadataPlugin).Assembly);

        Assert.Equal("SkyCD.Plugin.AudioCd", plugin.Id);
        Assert.Equal("Audio CD Metadata Collection", plugin.Name);
        Assert.Contains(plugin.Capabilities, capability => capability is IAudioCdMetadataCapability);
        Assert.Contains(plugin.Capabilities,
            capability => capability is SkyCD.SDK.Capabilities.Configuration.IPluginConfigurationCapability);
        Assert.Equal("cda", plugin.PrimaryExtension);
    }

    [Fact]
    public void PluginProject_ShipsNoDiscoveryManifest()
    {
        var projectDirectory = FindPluginProjectDirectory();

        Assert.False(File.Exists(Path.Combine(projectDirectory, "plugin.json")));
        Assert.Empty(Directory.EnumerateFiles(projectDirectory, "plugin.json", SearchOption.AllDirectories));
    }

    private static async Task<AudioCdCapture?> CaptureAsync(AudioCdSettings settings, DiscMetadata? mb = null,
        DiscMetadata? gnu = null, DiscMetadata? metadata = null, FakeMusicBrainzLookup? musicBrainz = null,
        FakeGnuDbLookup? gnuDb = null, FakeCoverArtLookup? cover = null)
    {
        var musicBrainzLookup = musicBrainz ?? new FakeMusicBrainzLookup(mb ?? metadata);
        var gnudbLookup = gnuDb ?? new FakeGnuDbLookup(gnu ?? metadata);
        var plugin = CreatePlugin(new FakeDiscTocReader(AudioCdFixtures.ExampleToc()),
            _ => new AudioCdMetadataResolver(musicBrainzLookup, gnudbLookup,
                cover ?? new FakeCoverArtLookup(AudioCdFixtures.CoverPng(300)), new AudioCdCoverRenderer()));

        return await plugin.CaptureAsync(
            new AudioCdDiscRequest("E:\\", settings.ToSettings()), null, CancellationToken.None);
    }

    private static AudioCdMetadataPlugin CreatePlugin(FakeDiscTocReader reader,
        Func<AudioCdSettings, AudioCdMetadataResolver> factory) => new(reader, factory);

    private static MediaMetadataValue? Find(IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.SingleOrDefault(value => value.Key == key);

    private static string? Text(IReadOnlyList<MediaMetadataValue> values, string key) => Find(values, key)?.Text;

    private static long Integer(IReadOnlyList<MediaMetadataValue> values, string key) =>
        Find(values, key)?.Integer ?? throw new InvalidOperationException($"'{key}' is not an integer.");

    private static double Number(IReadOnlyList<MediaMetadataValue> values, string key) =>
        Find(values, key)?.Number ?? throw new InvalidOperationException($"'{key}' is not a number.");

    private static int LongestEdge(byte[] png)
    {
        using var decoded = SKBitmap.Decode(png);
        return Math.Max(decoded!.Width, decoded.Height);
    }

    private static string FindPluginProjectDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins", "SkyCD.Plugin.Metadata.AudioCD");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the audio CD plugin project directory.");
    }

    private sealed class HttpRequestExceptionLike : Exception
    {
    }
}
