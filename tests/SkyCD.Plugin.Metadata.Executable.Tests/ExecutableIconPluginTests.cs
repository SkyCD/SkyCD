using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Models;
using SkyCD.Plugin.Metadata.Executable.Parsing;

namespace SkyCD.Plugin.Metadata.Executable.Tests;

/// <summary>
/// Index-time icon extraction: the stored artifact format and size, the choice between icon sizes, PNG images
/// inside icons, and every "no usable icon" case that must leave the item on its type fallback.
/// </summary>
public sealed class ExecutableIconPluginTests
{
    [Fact]
    public void Descriptor_DeclaresAnIndexTimePreviewWithItsOwnSchema()
    {
        var descriptor = new ExecutableIconPlugin().Descriptor;

        Assert.True(descriptor.IndexTimePreview);
        Assert.False(descriptor.IsOptIn);
        Assert.Equal(ExecutableSchema.PreviewSchemaId, descriptor.Schema.Id);
        Assert.Same(ExecutableSchema.Extensions, descriptor.Extensions);
    }

    [Fact]
    public async Task GeneratePreview_EncodesTheIconAsASquareBoundedPng()
    {
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Dib(16, 16, ExecutableFixtures.Pixel32Dib(16, 16, 0xFF0000, 0)),
            ExecutableFixtures.IconEntry.Dib(256, 256, ExecutableFixtures.Pixel32Dib(256, 256, 0x00FF00, 0)));

        var artifact = Assert.Single(await Generate(image));

        Assert.Equal("image/png", artifact.ContentType);
        var png = Decode(artifact);
        Assert.Equal(ExecutablePluginOptions.MaxIconSize, png.Width);
        Assert.Equal(ExecutablePluginOptions.MaxIconSize, png.Height);
        var centre = PixelAt(png, png.Width / 2, png.Height / 2);
        Assert.True((centre >> 16 & 0xFF) > 0xE0, "the 256px image must be decoded, not skipped by the pixel bound");
        Assert.Equal(255u, centre & 0xFF);
    }

    [Fact]
    public async Task GeneratePreview_KeepsTheIconTransparency()
    {
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Dib(16, 16, ExecutableFixtures.Pixel32Dib(16, 16, 0x123456, 4)));

        var png = Decode(Assert.Single(await Generate(image)));

        Assert.Equal(255u, PixelAt(png, png.Width / 2, png.Height / 2) & 0xFF);
        Assert.Equal(0u, PixelAt(png, 0, 0) & 0xFF);
    }

    [Fact]
    public async Task GeneratePreview_PrefersTheLargestIconImage()
    {
        var small = ExecutableFixtures.Pixel32Dib(16, 16, 0xFF0000, 0);
        var large = ExecutableFixtures.Pixel32Dib(64, 64, 0x00FF00, 0);
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Dib(16, 16, small),
            ExecutableFixtures.IconEntry.Dib(64, 64, large));

        var artifact = Assert.Single(await Generate(image));
        var png = Decode(artifact);
        var centre = PixelAt(png, 64, 64);

        Assert.True((centre >> 16 & 0xFF) > (centre >> 24 & 0xFF), "the green 64px image must win over red");
    }

    [Fact]
    public async Task GeneratePreview_DecodesAPngIconImage()
    {
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Png(32, 32, ExecutableFixtures.PngImage(32, 32, 0x0000FFFF)));

        var artifact = Assert.Single(await Generate(image));
        var png = Decode(artifact);
        var centre = PixelAt(png, 64, 64);

        Assert.True((centre & 0xFF) != 0, "the icon must keep its alpha");
        Assert.True((centre >> 8 & 0xFF) > 0xE0, "the blue channel of the source must survive");
    }

    [Fact]
    public async Task GeneratePreview_FallsBackToTheNextGroupWhenTheLargestIsDamaged()
    {
        var good = ExecutableFixtures.Pixel32Dib(16, 16, 0x3366CC, 0);
        var image = ExecutableFixtures.BuildGroups(
            ExecutableFixtures.Group(1, ExecutableFixtures.IconEntry.Dib(64, 64, [1, 2, 3, 4])),
            ExecutableFixtures.Group(2, ExecutableFixtures.IconEntry.Dib(16, 16, good)));

        Assert.Single(await Generate(image));
    }

    [Theory]
    [InlineData("plain")]
    [InlineData("mz-only")]
    [InlineData("no-resources")]
    [InlineData("truncated")]
    [InlineData("group-only")]
    [InlineData("empty-payload")]
    [InlineData("bad-png")]
    [InlineData("garbage-dib")]
    public async Task GeneratePreview_UnusableSources_ProduceNoArtifact(string scenario)
    {
        var image = scenario switch
        {
            "plain" => "Not a program"u8.ToArray(),
            "mz-only" => MzOnly(),
            "no-resources" => ExecutableFixtures.Build(),
            "truncated" => ExecutableFixtures.WithIcons(
                ExecutableFixtures.IconEntry.Dib(16, 16, ExecutableFixtures.Pixel32Dib(16, 16, 0x123456, 0)))
                is var full ? full[..(ExecutableFixtures.DosStubSize + 40)] : [],
            "group-only" => ExecutableFixtures.BuildResources(
                new ExecutableFixtures.Resource(ExecutableFixtures.ImageTypeIconGroup, 1,
                    ExecutableFixtures.GroupDirectory([ExecutableFixtures.IconEntry.Dib(16, 16, [1])]))),
            "empty-payload" => ExecutableFixtures.BuildResources(
                new ExecutableFixtures.Resource(ExecutableFixtures.ImageTypeIconGroup, 1,
                    ExecutableFixtures.GroupDirectory([ExecutableFixtures.IconEntry.Dib(16, 16, [])])),
                new ExecutableFixtures.Resource(ExecutableFixtures.ImageTypeIcon, 1, [])),
            "bad-png" => ExecutableFixtures.WithIcons(
                ExecutableFixtures.IconEntry.Png(32, 32, [0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0])),
            "garbage-dib" => ExecutableFixtures.WithIcons(
                ExecutableFixtures.IconEntry.Dib(16, 16, Enumerable.Range(0, 128).Select(value => (byte)value)
                    .ToArray())),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        Assert.Empty(await Generate(image));
    }

    [Fact]
    public async Task GeneratePreview_HugeSource_IsSkippedByTheIconBound()
    {
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Dib(16, 16, ExecutableFixtures.Pixel32Dib(16, 16, 0x123456, 0)));
        var padded = new byte[image.Length + 4096];
        image.CopyTo(padded, 0);
        var plugin = new ExecutableIconPlugin(new ExecutablePluginOptions(maxSourceBytes: 2048));

        var result = await plugin.GeneratePreviewAsync(new MediaItem(".exe", null, padded.Length),
            new ByteArrayMediaSource(padded), progress: null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task GeneratePreview_RejectsPngAboveTheConfiguredBound()
    {
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Png(64, 64, ExecutableFixtures.PngImage(64, 64, 0x0000FFFF)));
        var plugin = new ExecutableIconPlugin(new ExecutablePluginOptions(maxPngBytes: 64));

        var result = await plugin.GeneratePreviewAsync(new MediaItem(".exe", null, image.Length),
            new ByteArrayMediaSource(image), progress: null, CancellationToken.None);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task GeneratePreview_RejectsPngWhoseHeaderExceedsThePixelBound()
    {
        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Png(256, 256, ExecutableFixtures.PngWithDeclaredSize(4096, 4096)));

        Assert.Empty(await Generate(image));
    }

    [Fact]
    public void Decode_RejectsPngWhoseHeaderExceedsThePixelBound()
    {
        var exception = Assert.Throws<ExecutableIconException>(() =>
            ExecutableIconDecoder.Decode(ExecutableFixtures.PngWithDeclaredSize(4096, 4096),
                ExecutablePluginOptions.MaximumIconPixels));

        Assert.Contains("pixel count", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratePreview_ClampsTheRenderedEdgeToTheSupportedRange()
    {
        Assert.Equal(16, new ExecutablePluginOptions(iconSize: 4).IconSize);
        Assert.Equal(ExecutablePluginOptions.MaxIconSize,
            new ExecutablePluginOptions(iconSize: 1024).IconSize);

        var image = ExecutableFixtures.WithIcons(
            ExecutableFixtures.IconEntry.Dib(16, 16, ExecutableFixtures.Pixel32Dib(16, 16, 0x123456, 0)));
        var plugin = new ExecutableIconPlugin(new ExecutablePluginOptions(iconSize: 32));
        var result = await plugin.GeneratePreviewAsync(new MediaItem(".exe", null, image.Length),
            new ByteArrayMediaSource(image), progress: null, CancellationToken.None);

        var png = Decode(Assert.Single(result.Artifacts));
        Assert.Equal(32, png.Width);
        Assert.Equal(32, png.Height);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Options_RejectNonPositiveBounds(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutablePluginOptions(maxIconBytes: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutablePluginOptions(maxPngBytes: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutablePluginOptions(maxSourceBytes: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutablePluginOptions(maxIconSourceBytes: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ExecutablePluginOptions(maxIndexedIconImages: value));
    }

    [Fact]
    public void Options_NeverLetTheIconBoundExceedTheMetadataBound()
    {
        var options = new ExecutablePluginOptions(maxSourceBytes: 1024, maxIconSourceBytes: 4096);

        Assert.Equal(1024, options.MaxIconSourceBytes);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<MediaPreviewArtifact>> Generate(
        byte[] image)
    {
        var result = await new ExecutableIconPlugin().GeneratePreviewAsync(
            new MediaItem(".exe", null, image.Length),
            new ByteArrayMediaSource(image),
            progress: null,
            CancellationToken.None);
        return result.Artifacts;
    }

    private static ExecutableIconImage Decode(MediaPreviewArtifact artifact) =>
        ExecutableIconDecoder.Decode(artifact.Data.Span, 1024 * 1024);

    private static uint PixelAt(ExecutableIconImage image, int x, int y)
    {
        var offset = ((y * image.Width) + x) * 4;
        return ((uint)image.Rgba[offset] << 24) | ((uint)image.Rgba[offset + 1] << 16) |
               ((uint)image.Rgba[offset + 2] << 8) | image.Rgba[offset + 3];
    }

    private static byte[] MzOnly()
    {
        var image = new byte[256];
        "MZ"u8.CopyTo(image);
        image[0x3C] = 0xFF;
        image[0x3D] = 0xFF;
        return image;
    }
}
