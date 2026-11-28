using System.Text.RegularExpressions;
using SkyCD.Plugin.Metadata.Audio.AudioCD;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Models;
using SkyCD.Plugin.Metadata.Audio.AudioCD.Parsing;

namespace SkyCD.Plugin.Metadata.Audio.Tests.AudioCD;

public sealed class DiscIdCalculatorTests
{
    [Fact]
    public void MusicBrainz_MatchesThePublishedExample()
    {
        // https://musicbrainz.org/doc/Disc_ID_Calculation#Audio_CD
        Assert.Equal("49HHV7Eb8UKF3aQiNmu1GR8vKTY-",
            DiscIdCalculator.ComputeMusicBrainzDiscId(AudioCdFixtures.ExampleToc()));
    }

    [Fact]
    public void MusicBrainz_IsStableAndUrlSafe()
    {
        var id = DiscIdCalculator.ComputeMusicBrainzDiscId(AudioCdFixtures.ExampleToc());

        Assert.Equal(28, id.Length);
        Assert.Matches(new Regex("^[A-Za-z0-9._-]{27}-$"), id);
        Assert.Equal(id, DiscIdCalculator.ComputeMusicBrainzDiscId(AudioCdFixtures.ExampleToc()));
    }

    [Fact]
    public void Cddb_PacksChecksumPlayingTimeAndTrackCount()
    {
        // Single track from 2 s to 12 s: checksum digitSum(2) % 0xFF = 2, playing time 10 s, 1 track.
        var toc = new DiscToc(1, [150], 900);

        Assert.Equal("02000a01", DiscIdCalculator.ComputeCddbDiscId(toc));
    }

    [Fact]
    public void Cddb_IsLowercaseEightDigitHex()
    {
        var id = DiscIdCalculator.ComputeCddbDiscId(AudioCdFixtures.ExampleToc());

        Assert.Matches(new Regex("^[0-9a-f]{8}$"), id);
    }

    [Fact]
    public void InvalidToc_IsRejectedWithTheDomainException()
    {
        var toc = new DiscToc(1, [150, 100], 95462);

        Assert.Throws<AudioCdTocException>(() => DiscIdCalculator.ComputeMusicBrainzDiscId(toc));
        Assert.Throws<AudioCdTocException>(() => DiscIdCalculator.ComputeCddbDiscId(toc));
    }
}
