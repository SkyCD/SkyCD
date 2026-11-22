using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Audio.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Audio.Models;

namespace SkyCD.Plugin.Metadata.Audio.Tests;

public class AudioCoverArtPluginTests
{
    [Fact]
    public void Descriptor_SharesTheAudioTypesWithAMatchingPreviewSchema()
    {
        var descriptor = new AudioCoverArtPlugin().Descriptor;

        Assert.Equal(AudioSchema.Extensions, descriptor.Extensions);
        Assert.Equal("skycd.audio.preview", descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Empty(descriptor.Schema.Fields);
    }

    [Fact]
    public async Task Mp3_ReturnsTheEmbeddedPictureUnmodified()
    {
        var result = await RenderAsync(AudioFixtures.Mp3(), ".mp3", "audio/mpeg");

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal(AudioFixtures.JpegBytes, artifact.Data.ToArray());
    }

    [Fact]
    public async Task Flac_ReturnsThePictureBlockUnmodified()
    {
        var result = await RenderAsync(AudioFixtures.Flac(), ".flac", "audio/flac");

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.Equal(AudioFixtures.JpegBytes, artifact.Data.ToArray());
    }

    [Fact]
    public async Task Ogg_DecodesTheBase64PictureBlock()
    {
        var result = await RenderAsync(AudioFixtures.OggVorbis(), ".ogg", "audio/ogg");

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(AudioFixtures.PngBytes, artifact.Data.ToArray());
    }

    [Fact]
    public async Task M4a_ReturnsTheCoverAtom()
    {
        var result = await RenderAsync(AudioFixtures.M4a(), ".m4a", "audio/mp4");

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.Equal(AudioFixtures.PngBytes, artifact.Data.ToArray());
    }

    [Fact]
    public async Task SourceWithoutCoverArt_ReturnsNoArtifactRatherThanFailing()
    {
        var result = await RenderAsync(AudioFixtures.Mp3WithoutTags(), ".mp3", "audio/mpeg");

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task OversizedCoverArt_IsWithheldButStillReportedInMetadata()
    {
        var audio = AudioFixtures.Mp3();

        // The cover is withheld rather than truncated or re-encoded, so nothing misleading is stored...
        var preview = await new AudioCoverArtPlugin(new AudioPluginOptions(maxCoverArtBytes: 4))
            .GeneratePreviewAsync(new MediaItem(".mp3", "audio/mpeg", audio.Length),
                new ByteArrayMediaSource(audio), null, CancellationToken.None);
        Assert.Empty(preview.Artifacts);

        // ...and the metadata still says the source carries art, which is the honest "too large" state.
        var metadata = await AudioMetadataPluginTests.ExtractAsync(audio, ".mp3", "audio/mpeg");
        Assert.True(AudioMetadataPluginTests.Boolean(metadata, AudioSchema.HasCoverArt));
    }

    [Fact]
    public async Task CorruptSource_FailsWithADecodeError()
    {
        await Assert.ThrowsAsync<AudioDecodeException>(
            () => RenderAsync(AudioFixtures.NotAudio(), ".mp3", "audio/mpeg"));
    }

    [Fact]
    public async Task CancelledToken_StopsBeforeReadingTheSource()
    {
        var audio = AudioFixtures.Mp3();
        var source = new ByteArrayMediaSource(audio);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AudioCoverArtPlugin().GeneratePreviewAsync(
            new MediaItem(".mp3", "audio/mpeg", audio.Length), source, null, cancelled.Token));

        Assert.Equal(0, source.ChargedBytes);
    }

    private static async Task<MediaPreviewResult> RenderAsync(byte[] audio, string extension, string contentType) =>
        await new AudioCoverArtPlugin().GeneratePreviewAsync(
            new MediaItem(extension, contentType, audio.Length),
            new ByteArrayMediaSource(audio),
            progress: null,
            CancellationToken.None);
}
