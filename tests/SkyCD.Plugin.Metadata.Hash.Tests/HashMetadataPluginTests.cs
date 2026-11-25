using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Hash.Exceptions;
using Xunit;
using SkyCD.Plugin.Metadata.Hash.Models;

namespace SkyCD.Plugin.Metadata.Hash.Tests;

public class HashMetadataPluginTests
{
    private readonly HashMetadataPlugin plugin = new();

    [Fact]
    public void Descriptor_ClaimsAnySource_ButRemainsOptIn()
    {
        var descriptor = plugin.Descriptor;

        Assert.Contains("*", descriptor.Extensions);
        Assert.Contains("*", descriptor.ContentTypes);
        Assert.True(descriptor.IsOptIn);
        Assert.Equal(HashSchema.MetadataSchemaId, descriptor.Schema.Id);
        Assert.Equal([HashSchema.Ed2k, HashSchema.Tth, HashSchema.Sha256],
            descriptor.Schema.Fields.Select(field => field.Key));
    }

    [Fact]
    public async Task EmptyFile_ProducesIndependentKnownValues()
    {
        var result = await ExtractAsync([]);
        var values = result.ToDictionary(value => value.Key, value => value.Text);

        // MD4 of the empty message and SHA-256 of the empty message, independently published vectors.
        Assert.Equal("31d6cfe0d16ae931b73c59d7e0c089c0", values[HashSchema.Ed2k]);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", values[HashSchema.Sha256]);
        Assert.Equal(HashReference.Tth([]), values[HashSchema.Tth]);
        Assert.Equal(39, values[HashSchema.Tth]!.Length);
    }

    [Fact]
    public async Task SmallFile_MatchesPublishedMd4AndSha256Vectors()
    {
        var data = Encoding.ASCII.GetBytes("abc");
        var values = (await ExtractAsync(data)).ToDictionary(value => value.Key, value => value.Text);

        // RFC 1320 MD4 test vector and the standard SHA-256 "abc" vector.
        Assert.Equal("a448017aaf21d8525fc10ae87aa6729d", values[HashSchema.Ed2k]);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", values[HashSchema.Sha256]);
        Assert.Equal(HashReference.Tth(data), values[HashSchema.Tth]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(1025)]
    [InlineData(2047)]
    [InlineData(2048)]
    [InlineData(3073)]
    [InlineData(4096)]
    public async Task TigerTreeBoundaries_MatchReference(int length)
    {
        var data = Pattern(length);
        var values = (await ExtractAsync(data)).ToDictionary(value => value.Key, value => value.Text);

        Assert.Equal(HashReference.Tth(data), values[HashSchema.Tth]);
        Assert.Equal(HashReference.Ed2k(data), values[HashSchema.Ed2k]);
        Assert.Equal(HashReference.Sha256(data), values[HashSchema.Sha256]);
    }

    [Theory]
    [InlineData(HashReference.Ed2kChunkBytes - 1)]
    [InlineData(HashReference.Ed2kChunkBytes)]
    [InlineData(HashReference.Ed2kChunkBytes + 1)]
    [InlineData(HashReference.Ed2kChunkBytes * 2)]
    [InlineData(HashReference.Ed2kChunkBytes * 2 + 412)]
    public async Task Ed2kChunkBoundaries_MatchReference(int length)
    {
        var data = Pattern(length);
        var values = (await ExtractAsync(data)).ToDictionary(value => value.Key, value => value.Text);

        Assert.Equal(HashReference.Ed2k(data), values[HashSchema.Ed2k]);
        Assert.Equal(HashReference.Tth(data), values[HashSchema.Tth]);

        if (length == HashReference.Ed2kChunkBytes)
        {
            // A source ending exactly on a chunk boundary has no trailing empty chunk.
            Assert.Equal(Convert.ToHexStringLower(Md4Of(data, 0, data.Length)), values[HashSchema.Ed2k]);
        }
    }

    [Fact]
    public async Task LargeFile_IsStreamedInBoundedChunks()
    {
        var data = Pattern(11 * 1024 * 1024 + 17);
        var source = new TestMediaSource(data, maxReadBytes: 64 * 1024);
        var progress = new RecordingProgress();

        var result = await plugin.ExtractAsync(
            new MediaItem(".bin", null, data.Length), source, progress, CancellationToken.None);
        var values = result.Values.ToDictionary(value => value.Key, value => value.Text);

        Assert.True(source.ReadCount > 20);
        Assert.True(source.LargestRequestedCount <= 128 * 1024);
        Assert.Equal(HashReference.Ed2k(data), values[HashSchema.Ed2k]);
        Assert.Equal(HashReference.Tth(data), values[HashSchema.Tth]);
        Assert.Equal(HashReference.Sha256(data), values[HashSchema.Sha256]);
        Assert.NotEmpty(progress.Fractions);
        Assert.Equal(1d, progress.Fractions[^1]);
        Assert.Equal(progress.Fractions.OrderBy(fraction => fraction), progress.Fractions);
    }

    [Fact]
    public async Task TruncatedSource_FailsWithoutPublishingDigests()
    {
        var data = Pattern(5000);
        var source = new TestMediaSource(data, truncate: true);

        await Assert.ThrowsAsync<HashSourceTruncatedException>(() =>
            plugin.ExtractAsync(new MediaItem(".bin", null, data.Length), source, null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_StopsBeforeCompletion()
    {
        var data = Pattern(2 * 1024 * 1024);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(fraction =>
        {
            if (fraction > 0.1)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            plugin.ExtractAsync(new MediaItem(".bin", null, data.Length), new TestMediaSource(data),
                progress, cancellation.Token));
    }

    [Fact]
    public void SourceTruncationException_UsesFixedTemplate()
    {
        var exception = new HashSourceTruncatedException(10, 20);

        Assert.Equal("The source ended after 10 of 20 bytes while hashing.", exception.Message);
    }

    private async Task<System.Collections.Generic.IReadOnlyList<MediaMetadataValue>> ExtractAsync(byte[] data)
    {
        var result = await plugin.ExtractAsync(
            new MediaItem(".bin", "application/octet-stream", data.Length),
            new TestMediaSource(data),
            null,
            CancellationToken.None);
        return result.Values;
    }

    private static byte[] Pattern(int length)
    {
        var data = new byte[length];
        for (var index = 0; index < data.Length; index++)
        {
            data[index] = (byte)(index * 31 + 7);
        }

        return data;
    }

    private static byte[] Md4Of(byte[] data, int offset, int count)
    {
        var digest = new Org.BouncyCastle.Crypto.Digests.MD4Digest();
        digest.BlockUpdate(data, offset, count);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }

    private sealed class InlineProgress : IProgress<MediaProgress>
    {
        private readonly Action<double> onReport;

        public InlineProgress(Action<double> onReport) => this.onReport = onReport;

        public void Report(MediaProgress value) => onReport(value.Fraction);
    }
}
