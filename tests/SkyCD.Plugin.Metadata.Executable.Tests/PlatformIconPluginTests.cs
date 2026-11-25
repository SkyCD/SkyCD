using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Executable.Models;
using SkyCD.Plugin.Metadata.Executable.Parsing;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Metadata.Executable.Tests;

/// <summary>
/// Platform icon sources: macOS <c>.icns</c> containers, <c>.desktop</c> launcher icons resolved through the
/// local path, AppImage <c>.DirIcon</c> volumes, and the explicit "no icon" answer for plain ELF and Mach-O.
/// </summary>
public sealed class PlatformIconPluginTests
{
    [Fact]
    public void IconDescriptor_ClaimsThePlatformContainers()
    {
        var descriptor = new ExecutableIconPlugin().Descriptor;

        Assert.Same(ExecutableSchema.IconExtensions, descriptor.Extensions);
        Assert.Contains(".icns", descriptor.Extensions);
        Assert.Contains(".appimage", descriptor.Extensions);
        Assert.Equal(ExecutableSchema.PreviewSchemaVersion, descriptor.Schema.Version);
        Assert.True(descriptor.IndexTimePreview);
    }

    [Fact]
    public void LauncherDescriptor_ClaimsOnlyLauncherEntries()
    {
        var descriptor = new LauncherIconPlugin().Descriptor;

        Assert.Equal(new[] { ".desktop" }, descriptor.Extensions);
        Assert.True(descriptor.IndexTimePreview);
        Assert.False(descriptor.IsOptIn);
        Assert.Equal(ExecutableSchema.PreviewSchemaId, descriptor.Schema.Id);
    }

    [Fact]
    public async Task Icns_PublishesTheLargestDecodableChunk()
    {
        var icns = PlatformIconFixtures.Icns(
            PlatformIconFixtures.Chunk("ic07", ExecutableFixtures.PngImage(128, 128, 0xFF0000FF)),
            PlatformIconFixtures.Chunk("ic08", ExecutableFixtures.PngImage(256, 256, 0x00FF00FF)));

        var png = Decode(Assert.Single(await Generate(icns, ".icns")));

        var centre = PixelAt(png, png.Width / 2, png.Height / 2);
        Assert.True((centre >> 16 & 0xFF) > 0xE0, "the green 256px chunk must win over the red 128px one");
    }

    [Fact]
    public async Task Icns_SkipsAnImageAboveThePixelBoundAndUsesTheNextChunk()
    {
        var icns = PlatformIconFixtures.Icns(
            PlatformIconFixtures.Chunk("ic09", ExecutableFixtures.PngWithDeclaredSize(4096, 4096)),
            PlatformIconFixtures.Chunk("ic07", ExecutableFixtures.PngImage(128, 128, 0x0000FFFF)));

        var png = Decode(Assert.Single(await Generate(icns, ".icns")));

        Assert.True((PixelAt(png, png.Width / 2, png.Height / 2) & 0xFF) != 0);
    }

    [Fact]
    public async Task Icns_SkipsAChunkAboveTheByteBound()
    {
        var icns = PlatformIconFixtures.Icns(
            PlatformIconFixtures.Chunk("ic07", ExecutableFixtures.PngImage(128, 128, 0x0000FFFF)));

        var artifacts = await Generate(icns, ".icns", new ExecutablePluginOptions(maxIconBytes: 16));

        Assert.Empty(artifacts);
    }

    [Theory]
    [InlineData("header-only")]
    [InlineData("truncated-chunk")]
    [InlineData("no-decodable-chunk")]
    public async Task Icns_MalformedContainers_ProduceNoIcon(string scenario)
    {
        var icns = scenario switch
        {
            "header-only" => "icns"u8.ToArray().Concat(new byte[] { 0, 0, 0, 8 }).ToArray(),
            "truncated-chunk" => PlatformIconFixtures.Icns(
                PlatformIconFixtures.Chunk("ic07", ExecutableFixtures.PngImage(128, 128, 0x0000FFFF)))
                is var full ? full[..^16] : [],
            "no-decodable-chunk" => PlatformIconFixtures.Icns(
                PlatformIconFixtures.Chunk("ic07", "not a picture"u8.ToArray())),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        Assert.Empty(await Generate(icns, ".icns"));
    }

    [Fact]
    public async Task AppImage_PublishesTheDirIconFromTheIsoVolume()
    {
        var icon = ExecutableFixtures.PngImage(64, 64, 0x00FF00FF);
        var image = PlatformIconFixtures.AppImage(PlatformIconFixtures.Iso9660((".DirIcon", icon)));

        var png = Decode(Assert.Single(await Generate(image, ".appimage")));

        Assert.True((PixelAt(png, png.Width / 2, png.Height / 2) >> 16 & 0xFF) > 0xE0);
    }

    [Fact]
    public async Task AppImage_FallsBackToTheLargestIconInTheVolume()
    {
        var image = PlatformIconFixtures.AppImage(PlatformIconFixtures.Iso9660(
            ("app.png", ExecutableFixtures.PngImage(64, 64, 0x0000FFFF)),
            (Path.Combine("usr", "share", "icons", "hicolor", "128x128", "apps", "app.png"),
                ExecutableFixtures.PngImage(128, 128, 0xFF0000FF))));

        var png = Decode(Assert.Single(await Generate(image, ".appimage")));

        Assert.True((PixelAt(png, png.Width / 2, png.Height / 2) >> 24 & 0xFF) > 0xE0,
            "the larger themed icon must win over the root one");
    }

    [Fact]
    public async Task AppImage_TypeTwoPayload_ProducesNoIcon()
    {
        var image = PlatformIconFixtures.AppImage(PlatformIconFixtures.Iso9660(
            (".DirIcon", ExecutableFixtures.PngImage(64, 64, 0x0000FFFF))), type: 2);

        Assert.Empty(await Generate(image, ".appimage"));
    }

    [Fact]
    public async Task AppImage_DamagedVolume_ProducesNoIcon()
    {
        var image = PlatformIconFixtures.AppImage(new byte[64 * 1024]);

        Assert.Empty(await Generate(image, ".appimage"));
    }

    [Fact]
    public async Task PlainElfAndMachO_ProduceNoIcon()
    {
        Assert.Empty(await Generate(PlatformIconFixtures.Elf(), ".appimage"));
        Assert.Empty(await Generate(PlatformIconFixtures.MachO(), ".appimage"));
        Assert.Empty(await Generate(PlatformIconFixtures.MachO(), ".exe"));
    }

    [Fact]
    public async Task Launcher_ResolvesAnAbsoluteIconPath()
    {
        using var workspace = new TestWorkspace();
        var icon = WriteImage(workspace.Root, "icon.png", 64);
        var launcher = WriteLauncher(workspace.Root, $"Icon={icon}");

        var png = Decode(Assert.Single(await GenerateLauncher(launcher)));

        Assert.True((PixelAt(png, png.Width / 2, png.Height / 2) >> 16 & 0xFF) > 0xE0);
    }

    [Fact]
    public async Task Launcher_ResolvesASiblingIconFile()
    {
        using var workspace = new TestWorkspace();
        WriteImage(workspace.Root, "myapp.png", 64);
        var launcher = WriteLauncher(workspace.Root, "Icon=myapp.png");

        Assert.Single(await GenerateLauncher(launcher));
    }

    [Fact]
    public async Task Launcher_ResolvesARelativeIconPath()
    {
        using var workspace = new TestWorkspace();
        var icons = Directory.CreateDirectory(Path.Combine(workspace.Root, "icons"));
        File.WriteAllBytes(Path.Combine(icons.FullName, "app.png"),
            ExecutableFixtures.PngImage(64, 64, 0x00FF00FF));
        var launcher = WriteLauncher(workspace.Root, "Icon=icons/app.png");

        Assert.Single(await GenerateLauncher(launcher));
    }

    [Fact]
    public async Task Launcher_ResolvesAThemeNameFromTheVolume()
    {
        using var workspace = new TestWorkspace();
        var theme = Directory.CreateDirectory(Path.Combine(workspace.Root, "usr", "share", "icons", "hicolor",
            "128x128", "apps"));
        File.WriteAllBytes(Path.Combine(theme.FullName, "greenapp.png"),
            ExecutableFixtures.PngImage(128, 128, 0x00FF00FF));
        var launcher = WriteLauncher(workspace.Root, "Icon=greenapp", "apps");

        Assert.Single(await GenerateLauncher(launcher));
    }

    [Fact]
    public async Task Launcher_UnresolvedThemeName_ProducesNoIcon()
    {
        using var workspace = new TestWorkspace();
        var launcher = WriteLauncher(workspace.Root, "Icon=missingapp");

        Assert.Empty(await GenerateLauncher(launcher));
    }

    [Fact]
    public async Task Launcher_IgnoresLocalizedKeysAndDesktopActions()
    {
        using var workspace = new TestWorkspace();
        WriteImage(workspace.Root, "real.png", 64);
        var launcher = WriteLauncherFromLines(workspace.Root,
            "Icon[de]=missing.png",
            "Icon=real.png",
            "",
            "[Desktop Action new]",
            "Icon=missing.png");

        Assert.Single(await GenerateLauncher(launcher));
    }

    [Fact]
    public async Task Launcher_NonImageIcon_ProducesNoIcon()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(Path.Combine(workspace.Root, "fake.png"), "not an image");
        var launcher = WriteLauncher(workspace.Root, "Icon=fake.png");

        Assert.Empty(await GenerateLauncher(launcher));
    }

    private static async Task<IReadOnlyList<MediaPreviewArtifact>> Generate(byte[] image, string extension,
        ExecutablePluginOptions? options = null)
    {
        var plugin = new ExecutableIconPlugin(options ?? ExecutablePluginOptions.Default);
        var result = await plugin.GeneratePreviewAsync(new MediaItem(extension, null, image.Length),
            new ByteArrayMediaSource(image), progress: null, CancellationToken.None);
        return result.Artifacts;
    }

    private static async Task<IReadOnlyList<MediaPreviewArtifact>> GenerateLauncher(string path,
        ExecutablePluginOptions? options = null)
    {
        var plugin = new LauncherIconPlugin(options ?? ExecutablePluginOptions.Default);
        var result = await plugin.GeneratePreviewAsync(
            new LocalMediaContext(path, ".desktop", "application/x-desktop", new FileInfo(path).Length),
            progress: null, CancellationToken.None);
        return result.Artifacts;
    }

    private static ExecutableIconImage Decode(MediaPreviewArtifact artifact) =>
        ExecutableIconDecoder.DecodeEncodedImage(artifact.Data.Span, 1024 * 1024);

    private static uint PixelAt(ExecutableIconImage image, int x, int y)
    {
        var offset = ((y * image.Width) + x) * 4;
        return ((uint)image.Rgba[offset] << 24) | ((uint)image.Rgba[offset + 1] << 16) |
               ((uint)image.Rgba[offset + 2] << 8) | image.Rgba[offset + 3];
    }

    private static string WriteImage(string directory, string name, int edge)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, ExecutableFixtures.PngImage(edge, edge, 0x00FF00FF));
        return path;
    }

    private static string WriteLauncherFromLines(string directory, params string[] lines) =>
        WriteLauncher(directory, string.Join('\n', lines));

    private static string WriteLauncher(string directory, string body, string? subdirectory = null)
    {
        var launcherDirectory = subdirectory is null
            ? directory
            : Directory.CreateDirectory(Path.Combine(directory, subdirectory)).FullName;
        var path = Path.Combine(launcherDirectory, "app.desktop");
        File.WriteAllText(path, $"[Desktop Entry]\nType=Application\n{body}\n");
        return path;
    }
}
