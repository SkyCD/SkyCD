using System;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Export;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Metadata.GameDev.Tests;

public sealed class GameDevPreviewPluginTests
{
    [Fact]
    public void Pixel_preview_descriptors_are_index_time_and_claim_only_their_own_extensions()
    {
        IMediaPreviewCapability[] plugins =
        [
            new GameDevAsepritePreviewPlugin(),
            new GameDevUnityPackagePreviewPlugin(),
            new GameDevUnrealPreviewPlugin(),
            new GameDevWickedScenePreviewPlugin(),
            new GameDevXnbPreviewPlugin()
        ];

        Assert.All(plugins, plugin =>
        {
            Assert.True(plugin.Descriptor.IndexTimePreview);
            Assert.False(plugin.Descriptor.IsOptIn);
            Assert.Equal(GameDevSchema.PreviewSchemaId, plugin.Descriptor.Schema.Id);
            Assert.NotEmpty(plugin.Descriptor.Extensions);
        });

        Assert.Equal(new[] { ".aseprite", ".ase" }, new GameDevAsepritePreviewPlugin().Descriptor.Extensions);
        Assert.Equal(new[] { ".unitypackage" }, new GameDevUnityPackagePreviewPlugin().Descriptor.Extensions);
        Assert.Equal(new[] { ".uasset", ".umap" }, new GameDevUnrealPreviewPlugin().Descriptor.Extensions);
        Assert.Equal(new[] { ".wiscene" }, new GameDevWickedScenePreviewPlugin().Descriptor.Extensions);
        Assert.Equal(new[] { ".xnb" }, new GameDevXnbPreviewPlugin().Descriptor.Extensions);
    }

    [Fact]
    public void Structure_preview_runs_on_demand_and_covers_project_and_scene_files()
    {
        var descriptor = new GameDevStructurePreviewPlugin().Descriptor;

        Assert.False(descriptor.IndexTimePreview);
        Assert.Contains(".tscn", descriptor.Extensions);
        Assert.Contains(".uproject", descriptor.Extensions);
        Assert.Contains(".unity", descriptor.Extensions);
        Assert.Contains(".c3p", descriptor.Extensions);
        Assert.Equal(GameDevSchema.PreviewSchemaId, descriptor.Schema.Id);
    }

    [Fact]
    public async Task Unity_package_preview_passes_the_smallest_largest_preview_through()
    {
        var png = GameDevFixtures.PngPreview();
        var package = GameDevFixtures.UnityPackage(png);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevUnityPackagePreviewPlugin(),
            ".unitypackage", package);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.True(artifact.Data.Span.SequenceEqual(png));
    }

    [Fact]
    public async Task Unity_package_without_preview_yields_no_artifact()
    {
        var withoutPreview = GameDevFixtures.Zip([("empty.txt", GameDevFixtures.Utf8("nothing"))]);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevUnityPackagePreviewPlugin(),
            ".unitypackage", withoutPreview);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Unreal_package_preview_returns_the_stored_thumbnail_with_its_type()
    {
        var thumbnail = GameDevFixtures.PngPreview();
        var package = GameDevFixtures.UnrealPackage(thumbnail: thumbnail);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevUnrealPreviewPlugin(), ".uasset", package);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.True(artifact.Data.Span.SequenceEqual(thumbnail));
    }

    [Fact]
    public async Task Unreal_package_preview_detects_jpeg_thumbnails()
    {
        var jpeg = GameDevFixtures.FakeJpeg();
        var package = GameDevFixtures.UnrealPackage(thumbnail: jpeg);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevUnrealPreviewPlugin(), ".umap", package);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
    }

    [Fact]
    public async Task Wicked_scene_preview_publishes_the_embedded_jpeg()
    {
        var jpeg = GameDevFixtures.FakeJpeg();
        var scene = GameDevFixtures.WickedScene(jpeg);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevWickedScenePreviewPlugin(),
            ".wiscene", scene);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/jpeg", artifact.ContentType);
        Assert.True(artifact.Data.Span.SequenceEqual(jpeg));
    }

    [Fact]
    public async Task Wicked_scene_without_bytes_yields_no_artifact()
    {
        var scene = GameDevFixtures.WickedScene();
        var result = await GameDevTestHarness.PreviewAsync(new GameDevWickedScenePreviewPlugin(),
            ".wiscene", scene);

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Xnb_preview_renders_an_uncompressed_texture()
    {
        var xnb = GameDevFixtures.XnbTexture(4, 4);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevXnbPreviewPlugin(), ".xnb", xnb);

        var artifact = Assert.Single(result.Artifacts);
        Assert.StartsWith("image/", artifact.ContentType, StringComparison.Ordinal);
        Assert.True(artifact.Data.Length > 0);
    }

    [Fact]
    public async Task Xnb_preview_skips_a_compressed_payload()
    {
        var result = await GameDevTestHarness.PreviewAsync(new GameDevXnbPreviewPlugin(), ".xnb",
            GameDevFixtures.XnbCompressed());

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Aseprite_preview_skips_corrupt_documents()
    {
        var result = await GameDevTestHarness.PreviewAsync(new GameDevAsepritePreviewPlugin(), ".aseprite",
            GameDevFixtures.Utf8("not an aseprite file"));

        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task Structure_preview_renders_a_page_for_a_recognized_scene()
    {
        var scene = GameDevFixtures.Utf8(GameDevFixtures.GodotScene);
        var result = await GameDevTestHarness.PreviewAsync(new GameDevStructurePreviewPlugin(), ".tscn", scene);

        var artifact = Assert.Single(result.Artifacts);
        Assert.Equal("image/png", artifact.ContentType);
        Assert.True(artifact.Data.Length > 8);
        Assert.Equal(0x89, artifact.Data.Span[0]);
    }

    [Fact]
    public async Task Structure_preview_skips_unrecognized_and_invalid_sources()
    {
        var foreign = await GameDevTestHarness.PreviewAsync(new GameDevStructurePreviewPlugin(), ".unity",
            GameDevFixtures.Utf8("not a unity scene"));
        var garbage = await GameDevTestHarness.PreviewAsync(new GameDevStructurePreviewPlugin(), ".c3p",
            new byte[] { 1, 2, 3, 4 });

        Assert.Empty(foreign.Artifacts);
        Assert.Empty(garbage.Artifacts);
    }

    [Fact]
    public async Task Structure_summary_omits_absent_facts()
    {
        var facts = new GameDevFacts
        {
            Family = "Godot",
            Format = "Godot scene",
            NodeCount = 3,
            EngineVersion = "4.2"
        };

        var rows = GameDevStructurePageRenderer.BuildSummary(facts);

        Assert.Contains("Engine: 4.2", rows);
        Assert.Contains("Nodes: 3", rows);
        Assert.DoesNotContain(rows, row => row.StartsWith("Levels:", StringComparison.Ordinal));
        Assert.DoesNotContain(rows, row => row.StartsWith("Canvas:", StringComparison.Ordinal));
    }
}
