using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Audio.Exceptions;
using Xunit;
using SkyCD.Plugin.Audio.Models;

namespace SkyCD.Plugin.Audio.Tests;

public class AudioMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsTheDocumentedAudioTypesAndAVersionedSchema()
    {
        var descriptor = new AudioMetadataPlugin().Descriptor;

        Assert.Equal(AudioSchema.Extensions, descriptor.Extensions);
        Assert.Equal(AudioSchema.ContentTypes, descriptor.ContentTypes);
        Assert.Equal("skycd.audio.metadata", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Contains(descriptor.Extensions, extension => extension == ".mp3");
        Assert.Contains(descriptor.Extensions, extension => extension == ".flac");
        Assert.Contains(descriptor.Extensions, extension => extension == ".ogg");
        Assert.Contains(descriptor.Extensions, extension => extension == ".m4a");
    }

    [Fact]
    public async Task Mp3_ReadsId3v2TagsAndStreamFacts()
    {
        var result = await ExtractAsync(AudioFixtures.Mp3(frameCount: 4), ".mp3", "audio/mpeg");

        Assert.Equal("Test Title", Text(result, AudioSchema.Title));
        Assert.Equal("Test Artist", Text(result, AudioSchema.Artist));
        Assert.Equal("Test Album", Text(result, AudioSchema.Album));
        Assert.Equal("Rock", Text(result, AudioSchema.Genre));
        Assert.Equal(3L, Integer(result, AudioSchema.TrackNumber));
        Assert.Equal(12L, Integer(result, AudioSchema.TrackCount));
        Assert.Equal(1L, Integer(result, AudioSchema.DiscNumber));
        Assert.Equal(2L, Integer(result, AudioSchema.DiscCount));
        Assert.Equal(2021L, Integer(result, AudioSchema.Year));

        Assert.Equal("mp3", Text(result, AudioSchema.Container));
        Assert.Equal("mp3", Text(result, AudioSchema.Codec));
        Assert.Equal(44100L, Integer(result, AudioSchema.SampleRate));
        Assert.Equal(2L, Integer(result, AudioSchema.Channels));
        Assert.Equal(128L, Integer(result, AudioSchema.Bitrate));
        Assert.Equal("id3v2.3", Text(result, AudioSchema.TagSource));
        Assert.True(Boolean(result, AudioSchema.HasCoverArt));
    }

    [Fact]
    public async Task Mp3_DurationComesFromTheXingHeaderWhenPresent()
    {
        var result = await ExtractAsync(AudioFixtures.Mp3(frameCount: 100), ".mp3", "audio/mpeg");

        Assert.Equal(Math.Round(AudioFixtures.MpegDuration(100), 3), Number(result, AudioSchema.Duration));
    }

    [Fact]
    public async Task Mp3WithoutXing_FallsBackToAConstantBitrateEstimate()
    {
        var audio = AudioFixtures.Mp3(frameCount: 100, withXing: false, withId3v1: false);
        var result = await ExtractAsync(audio, ".mp3", "audio/mpeg");

        var duration = Number(result, AudioSchema.Duration);
        Assert.NotNull(duration);
        Assert.True(duration > 2.0 && duration < 3.2,
            $"The CBR estimate {duration}s is not near the true {AudioFixtures.MpegDuration(100):0.###}s.");
    }

    // ISO-8859-1 cannot represent the coffee emoji, so that case uses a Latin-1-safe string; the point of the
    // theory is that each declared encoding round-trips, not that every encoding carries every code point.
    [Theory]
    [InlineData((byte)0x00, "Café Ünicode")]
    [InlineData((byte)0x01, "Café ☕ Ünicode")]
    [InlineData((byte)0x02, "Café ☕ Ünicode")]
    [InlineData((byte)0x03, "Café ☕ Ünicode")]
    public async Task Mp3_DecodesEveryId3v2TextEncoding(byte encoding, string expectedTitle)
    {
        // No ID3v1 tail here: it is Latin-1 only and would legitimately merge a lossy copy of the same text.
        var audio = AudioFixtures.Mp3(
            withId3v1: false, title: expectedTitle, artist: "Ærøskøbing", textEncoding: encoding);

        var result = await ExtractAsync(audio, ".mp3", "audio/mpeg");

        Assert.Equal(expectedTitle, Text(result, AudioSchema.Title));
        Assert.Equal("Ærøskøbing", Text(result, AudioSchema.Artist));
    }

    [Fact]
    public async Task Mp3_MergesRepeatedFramesIntoOneMultiValueField()
    {
        var audio = AudioFixtures.Mp3WithTwoArtists();

        var result = await ExtractAsync(audio, ".mp3", "audio/mpeg");

        Assert.Equal("First Artist; Second Artist", Text(result, AudioSchema.Artist));
    }

    [Fact]
    public async Task Mp3WithId3v1Only_ReadsTheTailTagsAndGenreName()
    {
        var result = await ExtractAsync(AudioFixtures.Mp3WithId3v1Only(), ".mp3", "audio/mpeg");

        Assert.Equal("Tail Title", Text(result, AudioSchema.Title));
        Assert.Equal("Tail Artist", Text(result, AudioSchema.Artist));
        Assert.Equal("Tail Album", Text(result, AudioSchema.Album));
        Assert.Equal(1999L, Integer(result, AudioSchema.Year));
        Assert.Equal(7L, Integer(result, AudioSchema.TrackNumber));
        Assert.Equal("Rock", Text(result, AudioSchema.Genre));
        Assert.Equal("id3v1", Text(result, AudioSchema.TagSource));
    }

    [Fact]
    public async Task Mp3WithoutTags_OmitsTagFieldsInsteadOfEmittingBlanks()
    {
        var result = await ExtractAsync(AudioFixtures.Mp3WithoutTags(), ".mp3", "audio/mpeg");

        Assert.Equal("none", Text(result, AudioSchema.TagSource));
        Assert.Null(Text(result, AudioSchema.Title));
        Assert.Null(Text(result, AudioSchema.Artist));
        Assert.Null(Integer(result, AudioSchema.Year));
        Assert.False(Boolean(result, AudioSchema.HasCoverArt));

        // Stream facts and provenance are always known, so they are always present.
        Assert.Equal("mp3", Text(result, AudioSchema.Container));
        Assert.Equal(44100L, Integer(result, AudioSchema.SampleRate));
    }

    [Fact]
    public async Task Flac_ReadsStreamInfoVorbisCommentsAndCoverArt()
    {
        var result = await ExtractAsync(
            AudioFixtures.Flac(sampleRate: 96000, channels: 6, bitDepth: 24, totalSamples: 960000),
            ".flac", "audio/flac");

        Assert.Equal("flac", Text(result, AudioSchema.Container));
        Assert.Equal("flac", Text(result, AudioSchema.Codec));
        Assert.Equal(96000L, Integer(result, AudioSchema.SampleRate));
        Assert.Equal(6L, Integer(result, AudioSchema.Channels));
        Assert.Equal(24L, Integer(result, AudioSchema.BitDepth));
        Assert.Equal(10.0, Number(result, AudioSchema.Duration));

        Assert.Equal("Flac Title", Text(result, AudioSchema.Title));
        Assert.Equal("Flac Artist", Text(result, AudioSchema.Artist));
        Assert.Equal("Flac Album", Text(result, AudioSchema.Album));
        Assert.Equal("Ambient", Text(result, AudioSchema.Genre));
        Assert.Equal(5L, Integer(result, AudioSchema.TrackNumber));
        Assert.Equal(11L, Integer(result, AudioSchema.TrackCount));
        Assert.Equal(2L, Integer(result, AudioSchema.DiscNumber));
        Assert.Equal(3L, Integer(result, AudioSchema.DiscCount));
        Assert.Equal(2019L, Integer(result, AudioSchema.Year));
        Assert.Equal("vorbis-comment", Text(result, AudioSchema.TagSource));
        Assert.True(Boolean(result, AudioSchema.HasCoverArt));
    }

    [Fact]
    public async Task Ogg_ReadsIdentificationCommentsAndDurationFromTheFinalPage()
    {
        var result = await ExtractAsync(
            AudioFixtures.OggVorbis(sampleRate: 48000, channels: 2, totalSamples: 480000),
            ".ogg", "audio/ogg");

        Assert.Equal("ogg", Text(result, AudioSchema.Container));
        Assert.Equal("vorbis", Text(result, AudioSchema.Codec));
        Assert.Equal(48000L, Integer(result, AudioSchema.SampleRate));
        Assert.Equal(2L, Integer(result, AudioSchema.Channels));
        Assert.Equal(128L, Integer(result, AudioSchema.Bitrate));
        Assert.Equal(10.0, Number(result, AudioSchema.Duration));

        Assert.Equal("Ogg Title", Text(result, AudioSchema.Title));
        Assert.Equal("Ogg Artist", Text(result, AudioSchema.Artist));
        Assert.Equal("Ogg Album", Text(result, AudioSchema.Album));
        Assert.Equal("vorbis-comment", Text(result, AudioSchema.TagSource));
        Assert.True(Boolean(result, AudioSchema.HasCoverArt));
    }

    [Fact]
    public async Task M4a_ReadsMovieHeaderSampleEntryAndItunesAtoms()
    {
        var result = await ExtractAsync(AudioFixtures.M4a(), ".m4a", "audio/mp4");

        Assert.Equal("mp4", Text(result, AudioSchema.Container));
        Assert.Equal("aac", Text(result, AudioSchema.Codec));
        Assert.Equal(4.0, Number(result, AudioSchema.Duration));
        Assert.Equal(44100L, Integer(result, AudioSchema.SampleRate));
        Assert.Equal(2L, Integer(result, AudioSchema.Channels));
        Assert.Equal(16L, Integer(result, AudioSchema.BitDepth));

        Assert.Equal("M4a Title", Text(result, AudioSchema.Title));
        Assert.Equal("M4a Artist", Text(result, AudioSchema.Artist));
        Assert.Equal("M4a Album", Text(result, AudioSchema.Album));
        Assert.Equal("M4a Album Artist", Text(result, AudioSchema.AlbumArtist));
        Assert.Equal(6L, Integer(result, AudioSchema.TrackNumber));
        Assert.Equal(9L, Integer(result, AudioSchema.TrackCount));
        Assert.Equal(1L, Integer(result, AudioSchema.DiscNumber));
        Assert.Equal(2018L, Integer(result, AudioSchema.Year));
        Assert.Equal("itunes-atom", Text(result, AudioSchema.TagSource));
        Assert.True(Boolean(result, AudioSchema.HasCoverArt));
    }

    [Fact]
    public async Task M4aWithoutTags_ReportsNoTagsButKeepsStreamFacts()
    {
        var result = await ExtractAsync(AudioFixtures.M4a(withTags: false), ".m4a", "audio/mp4");

        Assert.Equal("none", Text(result, AudioSchema.TagSource));
        Assert.Null(Text(result, AudioSchema.Title));
        Assert.Equal(4.0, Number(result, AudioSchema.Duration));
        Assert.Equal(2L, Integer(result, AudioSchema.Channels));
    }

    [Fact]
    public async Task M4aAppleLossless_ReportsItsCodecRatherThanFailing()
    {
        var result = await ExtractAsync(AudioFixtures.M4a(codec: "alac", bitDepth: 24), ".m4a", "audio/mp4");

        Assert.Equal("alac", Text(result, AudioSchema.Codec));
        Assert.Equal(24L, Integer(result, AudioSchema.BitDepth));
    }

    [Fact]
    public async Task NumericId3Genre_IsResolvedToItsName()
    {
        var audio = AudioFixtures.Mp3(genre: "(17)");

        var result = await ExtractAsync(audio, ".mp3", "audio/mpeg");

        Assert.Equal("Rock", Text(result, AudioSchema.Genre));
    }

    [Theory]
    [InlineData("mp3", "audio/mpeg")]
    [InlineData("flac", "audio/flac")]
    [InlineData("ogg", "audio/ogg")]
    [InlineData("m4a", "audio/mp4")]
    public async Task ContainerIsDetectedFromBytes_EvenWithAWrongExtension(string fileName, string contentType)
    {
        var audio = fileName switch
        {
            "mp3" => AudioFixtures.Mp3(),
            "flac" => AudioFixtures.Flac(),
            "ogg" => AudioFixtures.OggVorbis(),
            _ => AudioFixtures.M4a()
        };

        // Catalogued with the wrong extension but the right MIME type: the bytes still decide.
        var result = await ExtractAsync(audio, ".unknown", contentType);

        Assert.Equal(fileName == "m4a" ? "mp4" : fileName, Text(result, AudioSchema.Container));
    }

    [Theory]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".flac", "audio/flac")]
    [InlineData(".ogg", "audio/ogg")]
    [InlineData(".m4a", "audio/mp4")]
    public async Task NonAudioBytes_FailWithADecodeErrorForEachClaimedExtension(string extension, string contentType)
    {
        // A file catalogued as audio whose bytes match no container must fail, not report an empty result.
        await Assert.ThrowsAsync<AudioDecodeException>(
            () => ExtractAsync(AudioFixtures.NotAudio(), extension, contentType));
    }

    [Theory]
    [InlineData(".mp3", "audio/mpeg")]
    [InlineData(".flac", "audio/flac")]
    [InlineData(".ogg", "audio/ogg")]
    [InlineData(".m4a", "audio/mp4")]
    public async Task TruncatedContainers_FailWithADecodeError(string extension, string contentType)
    {
        var audio = extension switch
        {
            ".mp3" => AudioFixtures.Mp3(),
            ".flac" => AudioFixtures.Flac(),
            ".ogg" => AudioFixtures.OggVorbis(),
            _ => AudioFixtures.M4a()
        };

        await Assert.ThrowsAsync<AudioDecodeException>(
            () => ExtractAsync(AudioFixtures.HeaderOnly(audio, 16), extension, contentType));
    }

    [Fact]
    public async Task EmptySource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<AudioDecodeException>(() => ExtractAsync([], ".mp3", "audio/mpeg"));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var audio = AudioFixtures.Mp3();
        var source = new ByteArrayMediaSource(audio);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AudioMetadataPlugin().ExtractAsync(
            new MediaItem(".mp3", "audio/mpeg", audio.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    [Fact]
    public async Task TagReading_OnlyTouchesTheHeadAndTailOfALargeFile()
    {
        // A FLAC whose audio payload dwarfs its metadata: reading tags must not charge for the whole file.
        var audio = AudioFixtures.Flac(audioBytes: 8 * 1024 * 1024);
        var source = new ByteArrayMediaSource(audio);

        var result = await new AudioMetadataPlugin().ExtractAsync(
            new MediaItem(".flac", "audio/flac", audio.Length), source, null, CancellationToken.None);

        Assert.Equal("Flac Title", Text(result, AudioSchema.Title));
        Assert.True(source.ChargedBytes < audio.Length / 4,
            $"Tag reading charged {source.ChargedBytes} of {audio.Length} bytes; it should stay near the header.");
    }

    [Fact]
    public async Task ExhaustedHostBudget_SurfacesAsAProviderFailure()
    {
        var audio = AudioFixtures.Mp3();
        var source = new ByteArrayMediaSource(audio, budget: 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new AudioMetadataPlugin().ExtractAsync(
            new MediaItem(".mp3", "audio/mpeg", audio.Length), source, null, CancellationToken.None));
    }

    [Fact]
    public async Task EveryReturnedValue_ConformsToTheDeclaredSchema()
    {
        var schema = AudioSchema.Metadata;

        foreach (var audio in new[]
                 {
                     AudioFixtures.Mp3(), AudioFixtures.Flac(), AudioFixtures.OggVorbis(), AudioFixtures.M4a()
                 })
        {
            var result = await ExtractAsync(audio, ".mp3", null);

            Assert.NotEmpty(result.Values);
            foreach (var value in result.Values)
            {
                var field = schema.Fields.SingleOrDefault(candidate => candidate.Key == value.Key);
                Assert.NotNull(field);
                Assert.Equal(field!.Type, value.Type);
            }

            Assert.Equal(result.Values.Count,
                result.Values.Select(value => value.Key).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public async Task Extraction_LeavesTheSourceBytesUnchanged()
    {
        var audio = AudioFixtures.Mp3();
        var before = (byte[])audio.Clone();

        await ExtractAsync(audio, ".mp3", "audio/mpeg");

        Assert.Equal(before, audio);
    }

    internal static Task<MediaExtractionResult> ExtractAsync(byte[] audio, string extension, string? contentType) =>
        new AudioMetadataPlugin().ExtractAsync(
            new MediaItem(extension, contentType, audio.Length),
            new ByteArrayMediaSource(audio),
            progress: null,
            CancellationToken.None);

    internal static string? Text(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Text;

    internal static long? Integer(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Integer;

    internal static double? Number(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Number;

    internal static bool? Boolean(MediaExtractionResult result, string key) =>
        result.Values.FirstOrDefault(value => value.Key == key)?.Boolean;
}
