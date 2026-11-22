using System.Linq;
using SkyCD.Plugin.Metadata.AudioCD;
using SkyCD.Plugin.Metadata.AudioCD.Sources;
using Xunit;
using SkyCD.Plugin.Metadata.AudioCD.Configuration;
using SkyCD.Plugin.Metadata.AudioCD.Models;

namespace SkyCD.Plugin.Metadata.AudioCD.Tests;

public sealed class AudioCdSettingsTests
{
    [Fact]
    public void MissingSettings_UseTheDocumentedDefaults()
    {
        var settings = AudioCdSettings.Parse(AudioCdFixtures.Settings());

        Assert.Equal([AudioCdSource.MusicBrainz, AudioCdSource.GnuDb], settings.Sources);
        Assert.True(settings.DiscArtistTags);
        Assert.True(settings.TrackArtistTags);
        Assert.True(settings.DiscCovers);
        Assert.True(settings.TrackCovers);
        Assert.Equal(250, settings.DiscCoverSize);
        Assert.Equal(250, settings.TrackCoverSize);
        Assert.False(settings.Offline);
        Assert.Equal(8, settings.RequestTimeoutSeconds);
    }

    [Fact]
    public void CoverSizes_AreClampedToTheSupportedRange()
    {
        var small = AudioCdSettings.Parse(AudioCdFixtures.Settings(
            (AudioCdSettings.DiscCoverSizeKey, "1"),
            (AudioCdSettings.TrackCoverSizeKey, "99999")));
        var junk = AudioCdSettings.Parse(AudioCdFixtures.Settings(
            (AudioCdSettings.DiscCoverSizeKey, "not a number")));

        Assert.Equal(AudioCdSettings.MinCoverSize, small.DiscCoverSize);
        Assert.Equal(AudioCdSettings.MaxCoverSize, small.TrackCoverSize);
        Assert.Equal(AudioCdSettings.DefaultCoverSize, junk.DiscCoverSize);
    }

    [Fact]
    public void DisabledSources_StayDisabled_AndUnknownSourcesAreIgnored()
    {
        var disabled = AudioCdSettings.Parse(AudioCdFixtures.Settings((AudioCdSettings.SourcesKey, "")));
        var mixed = AudioCdSettings.Parse(AudioCdFixtures.Settings(
            (AudioCdSettings.SourcesKey, "gnudb; napster")));

        Assert.Empty(disabled.Sources);
        Assert.Equal([AudioCdSource.GnuDb], mixed.Sources);
    }

    [Fact]
    public void RoundTrip_PreservesNormalizedValues()
    {
        var original = AudioCdSettings.Parse(AudioCdFixtures.Settings(
            (AudioCdSettings.SourcesKey, "gnudb"),
            (AudioCdSettings.DiscArtistTagsKey, "false"),
            (AudioCdSettings.TrackCoversKey, "false"),
            (AudioCdSettings.DiscCoverSizeKey, "300"),
            (AudioCdSettings.TrackCoverSizeKey, "120"),
            (AudioCdSettings.OfflineKey, "true"),
            (AudioCdSettings.RequestTimeoutSecondsKey, "15")));

        var reparsed = AudioCdSettings.Parse(original.ToSettings());

        Assert.Equal([AudioCdSource.GnuDb], reparsed.Sources);
        Assert.False(reparsed.DiscArtistTags);
        Assert.False(reparsed.TrackCovers);
        Assert.Equal(300, reparsed.DiscCoverSize);
        Assert.Equal(120, reparsed.TrackCoverSize);
        Assert.True(reparsed.Offline);
        Assert.Equal(15, reparsed.RequestTimeoutSeconds);
    }

    [Fact]
    public void Editor_ProducesSettingsThatParseToTheSameValues()
    {
        var editor = new AudioCdConfigurationEditor(AudioCdFixtures.Settings())
        {
            MusicBrainzEnabled = false,
            TrackArtistTags = false,
            DiscCoverSize = 5000,
            RequestTimeoutSeconds = 0
        };

        var parsed = AudioCdSettings.Parse(editor.BuildSettings());

        Assert.Equal([AudioCdSource.GnuDb], parsed.Sources);
        Assert.False(parsed.TrackArtistTags);
        Assert.Equal(AudioCdSettings.MaxCoverSize, parsed.DiscCoverSize);
        Assert.Equal(1, parsed.RequestTimeoutSeconds);
        Assert.Single(parsed.ToSettings(), pair => pair.Key == AudioCdSettings.SourcesKey);
    }
}
