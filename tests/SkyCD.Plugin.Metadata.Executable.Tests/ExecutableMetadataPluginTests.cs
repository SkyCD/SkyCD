using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using Xunit;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Tests;

/// <summary>
/// Metadata extraction: header facts, version strings, icon facts and the graceful degradation of sources that
/// are not executables, are truncated or carry nothing to report.
/// </summary>
public sealed class ExecutableMetadataPluginTests
{
    [Fact]
    public void Descriptor_ClaimsExecutableExtensionsAndPublishesAVersionedSchema()
    {
        var descriptor = new ExecutableMetadataPlugin().Descriptor;

        Assert.Contains(".exe", descriptor.Extensions);
        Assert.Contains(".dll", descriptor.Extensions);
        Assert.False(descriptor.IsOptIn);
        Assert.False(descriptor.IndexTimePreview);
        Assert.Equal(ExecutableSchema.MetadataSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.Contains(descriptor.Schema.Fields, field => field.Key == ExecutableSchema.FileVersion);
        Assert.Contains(descriptor.Schema.Fields,
            field => field.Key == ExecutableSchema.LargestIconWidth && field.Unit == "px");
    }

    [Fact]
    public async Task Extract_PublishesHeaderFactsAndVersionStrings()
    {
        var image = ExecutableFixtures.WithVersion("1.2.3.4", "SkyCD Sample", "SkyCD");
        var values = await Extract(image, ".exe");

        Assert.Equal("pe", Text(values, ExecutableSchema.Container));
        Assert.Equal("x64", Text(values, ExecutableSchema.Architecture));
        Assert.Equal("PE32+", Text(values, ExecutableSchema.PeFormat));
        Assert.True(Flag(values, ExecutableSchema.IsExecutableImage));
        Assert.False(Flag(values, ExecutableSchema.IsDotNet));
        Assert.Equal("1.2.3.4", Text(values, ExecutableSchema.FileVersion));
        Assert.Equal("1.2.3.4", Text(values, ExecutableSchema.ProductVersion));
        Assert.Equal("SkyCD Sample", Text(values, ExecutableSchema.ProductName));
        Assert.Equal("SkyCD", Text(values, ExecutableSchema.CompanyName));
        Assert.Equal("skycd-fixture.exe", Text(values, ExecutableSchema.OriginalFilename));
        Assert.Equal(1, Integer(values, ExecutableSchema.IconImageCount));
        Assert.Equal(16, Integer(values, ExecutableSchema.LargestIconWidth));
        Assert.Equal(16, Integer(values, ExecutableSchema.LargestIconHeight));
    }

    [Fact]
    public async Task Extract_ReportsDotNetAndLibraryImages()
    {
        var image = ExecutableFixtures.Build(machine: ExecutableFixtures.MachineX86, pe32Plus: false,
            executable: false, dotNet: true);
        var values = await Extract(image, ".dll");

        Assert.Equal("x86", Text(values, ExecutableSchema.Architecture));
        Assert.Equal("PE32", Text(values, ExecutableSchema.PeFormat));
        Assert.False(Flag(values, ExecutableSchema.IsExecutableImage));
        Assert.True(Flag(values, ExecutableSchema.IsDotNet));
    }

    [Fact]
    public async Task Extract_BareDosExecutable_ReportsOnlyTheContainer()
    {
        var values = await Extract(DosOnlyImage(), ".com");

        Assert.Equal("mz-16", Text(values, ExecutableSchema.Container));
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.PeFormat);
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.IsDotNet);
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.IconImageCount);
    }

    [Fact]
    public async Task Extract_NonExecutableSource_ProducesNoValues()
    {
        var values = await Extract("just some text, not a program at all"u8.ToArray(), ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_EmptySource_ProducesNoValues()
    {
        var values = await Extract([], ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_TruncatedPeHeader_ProducesNoValuesRatherThanFailing()
    {
        var image = ExecutableFixtures.Build();
        var values = await Extract(image[..(ExecutableFixtures.DosStubSize + 8)], ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_TruncatedSectionTable_ProducesNoValuesRatherThanFailing()
    {
        // The header declares one 40-byte section entry, so cutting the last byte makes the table unreadable.
        var image = ExecutableFixtures.Build();
        var values = await Extract(image[..^1], ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_UnsupportedOptionalHeaderMagic_ProducesNoValues()
    {
        var image = ExecutableFixtures.WithOptionalHeaderMagic(ExecutableFixtures.Build(), 0x1234);
        var values = await Extract(image, ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_ImageWithoutSections_ProducesNoValues()
    {
        var values = await Extract(ExecutableFixtures.WithSectionCount(ExecutableFixtures.Build(), 0), ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_ImageWithoutDataDirectories_KeepsOnlyTheHeaderFacts()
    {
        var image = ExecutableFixtures.WithDataDirectoryCount(
            ExecutableFixtures.WithVersion("1.2.3.4", "SkyCD Sample", "SkyCD"), 0);
        var values = await Extract(image, ".exe");

        Assert.Equal("pe", Text(values, ExecutableSchema.Container));
        Assert.Equal("x64", Text(values, ExecutableSchema.Architecture));
        Assert.Equal("PE32+", Text(values, ExecutableSchema.PeFormat));
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.FileVersion);
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.IconImageCount);
    }

    [Fact]
    public async Task Extract_DataDirectoryCountBeyondTheHeader_KeepsTheHeaderAndResourceFacts()
    {
        // The optional header still carries the 16 standard directory entries; an inflated count must not throw
        // or hide the facts that follow.
        var image = ExecutableFixtures.WithDataDirectoryCount(
            ExecutableFixtures.WithVersion("1.2.3.4", "SkyCD Sample", "SkyCD"), uint.MaxValue);
        var values = await Extract(image, ".exe");

        Assert.Equal("pe", Text(values, ExecutableSchema.Container));
        Assert.Equal("1.2.3.4", Text(values, ExecutableSchema.FileVersion));
        Assert.Equal(1, Integer(values, ExecutableSchema.IconImageCount));
    }

    [Fact]
    public async Task Extract_DosImageWithoutPeSignature_ProducesNoValues()
    {
        var image = new byte[128];
        "MZ"u8.CopyTo(image);
        image[0x3C] = 0x50;

        var values = await Extract(image, ".exe");

        Assert.Empty(values.Values);
    }

    [Fact]
    public async Task Extract_ImageWithoutResources_HasHeaderFactsAndNoIconFacts()
    {
        var values = await Extract(ExecutableFixtures.Build(), ".exe");

        Assert.Equal("pe", Text(values, ExecutableSchema.Container));
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.IconImageCount);
    }

    [Fact]
    public async Task Extract_DamagedIconResources_KeepTheHeaderFacts()
    {
        var values = await Extract(
            ExecutableFixtures.BuildResources(
                new ExecutableFixtures.Resource(ExecutableFixtures.ImageTypeIconGroup, 1, [1, 2, 3])),
            ".exe");

        Assert.Equal("pe", Text(values, ExecutableSchema.Container));
        Assert.DoesNotContain(values.Values, value => value.Key == ExecutableSchema.IconImageCount);
    }

    [Fact]
    public async Task Extract_VersionStringsAreTruncatedToTheConfiguredLength()
    {
        var image = ExecutableFixtures.WithVersion("1.0.0.0", new string('P', 200), "SkyCD");
        var plugin = new ExecutableMetadataPlugin(new ExecutablePluginOptions(maxMetadataValueCharacters: 32));
        var values = await Run(plugin, image);

        Assert.Equal(32, Text(values, ExecutableSchema.ProductName)!.Length);
    }

    [Fact]
    public async Task Extract_ObservesCancellation()
    {
        var plugin = new ExecutableMetadataPlugin();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.ExtractAsync(
            new MediaItem(".exe", null, 10),
            new ByteArrayMediaSource(ExecutableFixtures.WithIcons(
                ExecutableFixtures.IconEntry.Dib(16, 16, ExecutableFixtures.Pixel32Dib(16, 16, 0x336699, 0)))),
            progress: null,
            cancelled.Token));
    }

    private static Task<MediaExtractionResult> Extract(byte[] image, string extension) =>
        Run(new ExecutableMetadataPlugin(), image, extension);

    private static async Task<MediaExtractionResult> Run(ExecutableMetadataPlugin plugin, byte[] image,
        string extension = ".exe")
    {
        var result = await plugin.ExtractAsync(
            new MediaItem(extension, null, image.Length),
            new ByteArrayMediaSource(image),
            progress: null,
            CancellationToken.None);
        return result;
    }

    private static byte[] DosOnlyImage()
    {
        // The DOS signature, then an offset far beyond the file: the image is a plain DOS program, not a PE.
        var image = new byte[256];
        "MZ"u8.CopyTo(image);
        image[0x3C] = 0xFF;
        image[0x3D] = 0xFF;
        return image;
    }

    private static string? Text(MediaExtractionResult result, string key) =>
        result.Values.SingleOrDefault(value => value.Key == key)?.Text;

    private static bool Flag(MediaExtractionResult result, string key) =>
        result.Values.Single(value => value.Key == key).Boolean!.Value;

    private static long Integer(MediaExtractionResult result, string key) =>
        result.Values.Single(value => value.Key == key).Integer!.Value;
}
