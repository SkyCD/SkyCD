using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using Xunit;

namespace SkyCD.Plugin.Metadata.GameDev.Tests;

public sealed class GameDevMetadataPluginTests
{
    [Fact]
    public void Descriptor_claims_every_supported_extension_exactly_once()
    {
        var descriptor = new GameDevMetadataPlugin().Descriptor;

        Assert.Equal(GameDevSchema.MetadataSchemaId, descriptor.Schema.Id);
        Assert.Equal(1, descriptor.Schema.Version);
        Assert.True(descriptor.Extensions.Count > 60);
        Assert.Equal(descriptor.Extensions.Count,
            descriptor.Extensions.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(descriptor.Extensions, extension => Assert.StartsWith(".", extension, StringComparison.Ordinal));
        Assert.Empty(descriptor.ContentTypes);
        Assert.False(descriptor.IsOptIn);
    }

    [Fact]
    public void Descriptor_claims_shared_extensions_behind_the_probe()
    {
        var claimed = new HashSet<string>(new GameDevMetadataPlugin().Descriptor.Extensions,
            StringComparer.OrdinalIgnoreCase);

        Assert.All(GameDevSchema.SharedExtensions, extension => Assert.Contains(extension, claimed));
    }

    [Fact]
    public async Task Godot_project_publishes_application_version_and_sections()
    {
        var values = await GameDevTestHarness.ExtractAsync(".godot", GameDevFixtures.GodotProject);

        Assert.Equal("Godot", values.Text(GameDevSchema.Family));
        Assert.Equal("Godot project", values.Text(GameDevSchema.Format));
        Assert.Equal("Star Hopper", values.Text(GameDevSchema.ProjectName));
        Assert.Equal("5", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal("4.2", values.Text(GameDevSchema.EngineVersion));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ImportCount));
        Assert.Equal("Game", values.Text(GameDevSchema.NameSamples));
    }

    [Fact]
    public async Task Godot_scene_counts_nodes_scripts_resources_and_connections()
    {
        var values = await GameDevTestHarness.ExtractAsync(".tscn", GameDevFixtures.GodotScene);

        Assert.Equal("Godot scene", values.Text(GameDevSchema.Format));
        Assert.Equal(2, values.Integer(GameDevSchema.NodeCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ScriptCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(2, values.Integer(GameDevSchema.ClassCount));
        Assert.Equal(1, values.Integer(GameDevSchema.BlockCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ChunkCount));
        Assert.Equal("3", values.Text(GameDevSchema.FormatVersion));
        Assert.Contains("Main", values.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Godot_resource_and_import_and_extension_keep_their_own_formats()
    {
        var resource = await GameDevTestHarness.ExtractAsync(".tres", GameDevFixtures.GodotResource);
        var import = await GameDevTestHarness.ExtractAsync(".import", GameDevFixtures.GodotImport);
        var extension = await GameDevTestHarness.ExtractAsync(".gdextension", GameDevFixtures.GodotExtension);

        Assert.Equal("Godot resource", resource.Text(GameDevSchema.Format));
        Assert.Equal("Animation", resource.Text(GameDevSchema.ItemName));
        Assert.Equal("Godot import", import.Text(GameDevSchema.Format));
        Assert.Equal("CompressedTexture2D", import.Text(GameDevSchema.ItemName));
        Assert.Equal(2, import.Integer(GameDevSchema.ResourceCount));
        Assert.Equal("Godot GDExtension", extension.Text(GameDevSchema.Format));
        Assert.Equal("4.1", extension.Text(GameDevSchema.EngineVersion));
        Assert.Equal(1, extension.Integer(GameDevSchema.ImportCount));
        Assert.Equal(1, extension.Integer(GameDevSchema.ResourceCount));
    }

    [Fact]
    public async Task Godot_binary_formats_read_engine_version_from_their_headers()
    {
        var resource = await GameDevTestHarness.ExtractAsync(".res", GameDevFixtures.GodotBinaryResource());
        var pack = await GameDevTestHarness.ExtractAsync(".pck", GameDevFixtures.GodotPack());

        Assert.Equal("Godot binary resource", resource.Text(GameDevSchema.Format));
        Assert.Equal("4.2.1", resource.Text(GameDevSchema.EngineVersion));
        Assert.Equal(true, resource.Boolean(GameDevSchema.Compiled));
        Assert.Equal("Godot package", pack.Text(GameDevSchema.Format));
        Assert.Equal("4.2.1", pack.Text(GameDevSchema.EngineVersion));
    }

    [Fact]
    public async Task Unity_scene_counts_game_objects_transforms_scripts_and_guids()
    {
        var values = await GameDevTestHarness.ExtractAsync(".unity", GameDevFixtures.UnityScene);

        Assert.Equal("Unity", values.Text(GameDevSchema.Family));
        Assert.Equal("Unity scene", values.Text(GameDevSchema.Format));
        Assert.Equal("1.1", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(5, values.Integer(GameDevSchema.ClassCount));
        Assert.Equal(2, values.Integer(GameDevSchema.EntityCount));
        Assert.Equal(1, values.Integer(GameDevSchema.NodeCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ScriptCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ImportCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal("Player", values.Text(GameDevSchema.ItemName));
        Assert.Contains("Ground", values.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unity_file_with_foreign_content_is_recognized_but_invalid()
    {
        var values = await GameDevTestHarness.ExtractAsync(".unity", "just some text, not a scene");

        Assert.Equal(false, values.Boolean(GameDevSchema.Valid));
        Assert.Null(values.Integer(GameDevSchema.ClassCount));
        Assert.Equal(4, values.Count);
    }

    [Fact]
    public async Task Tiled_xml_map_publishes_size_layers_objects_and_tilesets()
    {
        var values = await GameDevTestHarness.ExtractAsync(".tmx", GameDevFixtures.TiledMap);

        Assert.Equal("Tiled", values.Text(GameDevSchema.Family));
        Assert.Equal("Tiled map", values.Text(GameDevSchema.Format));
        Assert.Equal("1.11.0", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(40, values.Integer(GameDevSchema.MapWidth));
        Assert.Equal(30, values.Integer(GameDevSchema.MapHeight));
        Assert.Equal(16, values.Integer(GameDevSchema.TileWidth));
        Assert.Equal(2, values.Integer(GameDevSchema.LayerCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ObjectGroupCount));
        Assert.Equal(2, values.Integer(GameDevSchema.ObjectCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(1, values.Integer(GameDevSchema.BlockCount));
        Assert.Equal("tiles", values.Text(GameDevSchema.TilesetNames));
    }

    [Fact]
    public async Task Tiled_world_lists_its_maps_without_opening_them()
    {
        var values = await GameDevTestHarness.ExtractAsync(".world", GameDevFixtures.TiledWorld);

        Assert.Equal("Tiled world", values.Text(GameDevSchema.Format));
        Assert.Equal(2, values.Integer(GameDevSchema.SceneCount));
        Assert.Contains("level2.tmx", values.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tiled_json_map_matches_the_xml_key_semantics()
    {
        var values = await GameDevTestHarness.ExtractAsync(".tmj", GameDevFixtures.TiledJsonMap);

        Assert.Equal("Tiled map", values.Text(GameDevSchema.Format));
        Assert.Equal(20, values.Integer(GameDevSchema.MapWidth));
        Assert.Equal(2, values.Integer(GameDevSchema.LayerCount));
        Assert.Equal(2, values.Integer(GameDevSchema.ObjectCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal("overworld", values.Text(GameDevSchema.TilesetNames));
    }

    [Fact]
    public async Task Ldtk_project_counts_levels_worlds_entities_and_tilesets()
    {
        var values = await GameDevTestHarness.ExtractAsync(".ldtk", GameDevFixtures.LdtkProject);

        Assert.Equal("LDtk", values.Text(GameDevSchema.Family));
        Assert.Equal("LDtk project", values.Text(GameDevSchema.Format));
        Assert.Equal("Dungeon", values.Text(GameDevSchema.ProjectName));
        Assert.Equal(2, values.Integer(GameDevSchema.LevelCount));
        Assert.Equal(1, values.Integer(GameDevSchema.SceneCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ClassCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
    }

    [Fact]
    public async Task Unreal_project_accepts_comments_and_trailing_commas()
    {
        var values = await GameDevTestHarness.ExtractAsync(".uproject", GameDevFixtures.UnrealProject);

        Assert.Equal("Unreal", values.Text(GameDevSchema.Family));
        Assert.Equal("UProject", values.Text(GameDevSchema.Format));
        Assert.Equal("5.4", values.Text(GameDevSchema.EngineVersion));
        Assert.Equal("3", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ImportCount));
        Assert.Equal(2, values.Integer(GameDevSchema.SceneCount));
        Assert.Contains("StarHopper", values.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unreal_verse_counts_files_functions_and_classes()
    {
        var values = await GameDevTestHarness.ExtractAsync(".verse", GameDevFixtures.UnrealVerse);

        Assert.Equal("Verse", values.Text(GameDevSchema.Format));
        Assert.Equal(1, values.Integer(GameDevSchema.LabelCount));
        Assert.True(values.Integer(GameDevSchema.FileCount) >= 4);
    }

    [Fact]
    public async Task Unreal_package_publishes_cooked_state_counts_and_thumbnail_flag()
    {
        var data = GameDevFixtures.UnrealPackage(thumbnail: GameDevFixtures.FakeJpeg());
        var values = await GameDevTestHarness.ExtractAsync(".uasset", data);

        Assert.Equal("UPackage", values.Text(GameDevSchema.Format));
        Assert.Equal("BP_Player", values.Text(GameDevSchema.ProjectName));
        Assert.Equal(true, values.Boolean(GameDevSchema.Cooked));
        Assert.Equal(2, values.Integer(GameDevSchema.ExportCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ImportCount));
        Assert.Equal(3, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(true, values.Boolean(GameDevSchema.HasThumbnail));
    }

    [Fact]
    public async Task Unreal_package_with_foreign_magic_is_recognized_but_invalid()
    {
        var values = await GameDevTestHarness.ExtractAsync(".uasset", GameDevFixtures.Utf8("not a package"));
        var random = await GameDevTestHarness.ExtractAsync(".uasset", new byte[64]);

        Assert.Equal(false, values.Boolean(GameDevSchema.Valid));
        Assert.Equal(false, random.Boolean(GameDevSchema.Valid));
        Assert.Null(values.Integer(GameDevSchema.ExportCount));
    }

    [Fact]
    public async Task Xnb_texture_publishes_surface_size_and_platform()
    {
        var values = await GameDevTestHarness.ExtractAsync(".xnb", GameDevFixtures.XnbTexture(4, 3));

        Assert.Equal("XNB", values.Text(GameDevSchema.Format));
        Assert.Equal("5", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(4, values.Integer(GameDevSchema.ImageWidth));
        Assert.Equal(3, values.Integer(GameDevSchema.ImageHeight));
        Assert.Equal("Color", values.Text(GameDevSchema.ColorMode));
        Assert.Equal(true, values.Boolean(GameDevSchema.Compiled));
        Assert.Equal(true, values.Boolean(GameDevSchema.HasThumbnail));
        Assert.Contains("Windows", values.Text(GameDevSchema.Application), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compressed_xnb_publishes_header_facts_only()
    {
        var values = await GameDevTestHarness.ExtractAsync(".xnb", GameDevFixtures.XnbCompressed());

        Assert.Equal("XNB", values.Text(GameDevSchema.Format));
        Assert.Null(values.Integer(GameDevSchema.ImageWidth));
        Assert.Null(values.Boolean(GameDevSchema.HasThumbnail));
    }

    [Fact]
    public async Task Wicked_scene_reads_header_version_and_thumbnail_flag()
    {
        var values = await GameDevTestHarness.ExtractAsync(".wiscene",
            GameDevFixtures.WickedScene(GameDevFixtures.FakeJpeg()));

        Assert.Equal("Wicked Engine", values.Text(GameDevSchema.Family));
        Assert.Equal("1", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(true, values.Boolean(GameDevSchema.HasThumbnail));
    }

    [Fact]
    public async Task Roblox_xml_place_counts_items_and_reads_the_place_name()
    {
        var values = await GameDevTestHarness.ExtractAsync(".rbxlx", GameDevFixtures.RobloxPlace);

        Assert.Equal("Roblox", values.Text(GameDevSchema.Family));
        Assert.Equal("Roblox XML place", values.Text(GameDevSchema.Format));
        Assert.Equal(3, values.Integer(GameDevSchema.InstanceCount));
        Assert.Equal(3, values.Integer(GameDevSchema.ClassCount));
        Assert.Equal("Workspace", values.Text(GameDevSchema.ProjectName));
    }

    [Fact]
    public async Task Roblox_binary_place_reads_class_and_instance_counts()
    {
        var values = await GameDevTestHarness.ExtractAsync(".rbxl", GameDevFixtures.RobloxBinary(6, 42));

        Assert.Equal("Roblox binary place", values.Text(GameDevSchema.Format));
        Assert.Equal(6, values.Integer(GameDevSchema.ClassCount));
        Assert.Equal(42, values.Integer(GameDevSchema.InstanceCount));
    }

    [Fact]
    public async Task GameMaker_archive_merges_project_facts_with_entry_counts()
    {
        var values = await GameDevTestHarness.ExtractAsync(".yyz", GameDevFixtures.GameMakerArchive);

        Assert.Equal("GameMaker project archive", values.Text(GameDevSchema.Format));
        Assert.Equal("StarHopper", values.Text(GameDevSchema.ProjectName));
        Assert.Equal("2024.6.2.162", values.Text(GameDevSchema.EngineVersion));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(1, values.Integer(GameDevSchema.SceneCount));
        Assert.Equal(2, values.Integer(GameDevSchema.FileCount));
    }

    [Fact]
    public async Task GameMaker_project_and_resource_descriptors_read_their_own_keys()
    {
        var project = await GameDevTestHarness.ExtractAsync(".yyp", """
            {
              "name": "StarHopper",
              "IDEVersion": "2024.6.2.162",
              "resourceVersion": "2.0",
              "resources": [ { "id": { "name": "obj_player" } } ],
              "RoomOrderNodes": [ { "roomId": "rm_title" } ]
            }
            """);
        var resource = await GameDevTestHarness.ExtractAsync(".yy", """
            {
              "resourceType": "GMSprite",
              "name": "spr_player",
              "parent": { "name": "Sprites" }
            }
            """);

        Assert.Equal("StarHopper", project.Text(GameDevSchema.ProjectName));
        Assert.Equal(1, project.Integer(GameDevSchema.ResourceCount));
        Assert.Equal("GMSprite", resource.Text(GameDevSchema.Format));
        Assert.Equal("spr_player", resource.Text(GameDevSchema.ItemName));
    }

    [Fact]
    public async Task Renpy_script_counts_labels_characters_and_lines()
    {
        var values = await GameDevTestHarness.ExtractAsync(".rpy", GameDevFixtures.RenpyScript);

        Assert.Equal("Ren'Py script", values.Text(GameDevSchema.Format));
        Assert.Equal(2, values.Integer(GameDevSchema.LabelCount));
        Assert.Equal(1, values.Integer(GameDevSchema.CharacterCount));
        Assert.True(values.Integer(GameDevSchema.FileCount) >= 6);
    }

    [Fact]
    public async Task Renpy_archive_reads_version_without_unpickling_the_index()
    {
        var values = await GameDevTestHarness.ExtractAsync(".rpa", GameDevFixtures.RenpyArchive());

        Assert.Equal("Ren'Py archive", values.Text(GameDevSchema.Format));
        Assert.Equal("3.0", values.Text(GameDevSchema.FormatVersion));
        Assert.StartsWith("index 0x", values.Text(GameDevSchema.ItemName), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Renpy_compiled_script_without_magic_is_recognized_but_invalid()
    {
        var values = await GameDevTestHarness.ExtractAsync(".rpyc", GameDevFixtures.Utf8("not compiled"));

        Assert.Equal("Ren'Py compiled script", values.Text(GameDevSchema.Format));
        Assert.Equal(false, values.Boolean(GameDevSchema.Valid));
    }

    [Fact]
    public async Task Rpg_maker_project_marker_sets_the_maker_version()
    {
        var values = await GameDevTestHarness.ExtractAsync(".rmmzproject", GameDevFixtures.RpgMakerProject);

        Assert.Equal("RPG Maker", values.Text(GameDevSchema.Family));
        Assert.Equal("MV", values.Text(GameDevSchema.Format));
        Assert.Equal("1.6.1", values.Text(GameDevSchema.EngineVersion));
    }

    [Fact]
    public async Task Love_archive_reports_entry_count_main_script_and_version()
    {
        var values = await GameDevTestHarness.ExtractAsync(".love", GameDevFixtures.LoveArchive);

        Assert.Equal("LÖVE game", values.Text(GameDevSchema.Format));
        Assert.Equal(2, values.Integer(GameDevSchema.FileCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ScriptCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal("11.4", values.Text(GameDevSchema.EngineVersion));
    }

    [Fact]
    public async Task Construct_project_reads_layouts_from_project_c3proj_only()
    {
        var values = await GameDevTestHarness.ExtractAsync(".c3p", GameDevFixtures.ConstructProject);

        Assert.Equal("Construct 3 project", values.Text(GameDevSchema.Format));
        Assert.Equal("1.9", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal("41400", values.Text(GameDevSchema.EngineVersion));
        Assert.Equal(2, values.Integer(GameDevSchema.SceneCount));
        Assert.Equal(1, values.Integer(GameDevSchema.BlockCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ScriptCount));
        Assert.Equal(1, values.Integer(GameDevSchema.FileCount));
    }

    [Fact]
    public async Task Stride_scene_reads_tag_serializer_version_and_names()
    {
        var values = await GameDevTestHarness.ExtractAsync(".sdscene", GameDevFixtures.StrideScene);

        Assert.Equal("Stride", values.Text(GameDevSchema.Family));
        Assert.Equal("Stride scene", values.Text(GameDevSchema.Format));
        Assert.Equal("2.0.0.0", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(2, values.Integer(GameDevSchema.ResourceCount));
        Assert.Contains("Main Camera", values.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Monogame_content_project_counts_directives()
    {
        var values = await GameDevTestHarness.ExtractAsync(".mgcb", GameDevFixtures.MonoGameContentProject);

        Assert.Equal("MGCB content project", values.Text(GameDevSchema.Format));
        Assert.Equal("DesktopGL", values.Text(GameDevSchema.EngineVersion));
        Assert.Equal("Reach", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ScriptCount));
        Assert.Equal(1, values.Integer(GameDevSchema.ImportCount));
        Assert.Equal(1, values.Integer(GameDevSchema.FileCount));
    }

    [Fact]
    public async Task Libgdx_atlas_counts_pages_and_regions()
    {
        var values = await GameDevTestHarness.ExtractAsync(".atlas", GameDevFixtures.AtlasText);

        Assert.Equal("libGDX", values.Text(GameDevSchema.Family));
        Assert.Equal("Texture atlas", values.Text(GameDevSchema.Format));
        Assert.Equal("RGBA8888", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(1, values.Integer(GameDevSchema.FileCount));
        Assert.Equal(2, values.Integer(GameDevSchema.ClassCount));
        Assert.Equal(1024, values.Integer(GameDevSchema.ImageWidth));
    }

    [Fact]
    public async Task Text_over_the_prefix_bound_is_still_read_and_flagged_incomplete()
    {
        var scene = GameDevFixtures.GodotScene + "\n# " + new string('x', 5 * 1024 * 1024);
        var values = await GameDevTestHarness.ExtractAsync(".tscn", GameDevFixtures.Utf8(scene));

        Assert.Equal(true, values.Boolean(GameDevSchema.ScanIncomplete));
        Assert.Equal(2, values.Integer(GameDevSchema.NodeCount));
    }

    [Fact]
    public async Task Every_claimed_extension_with_garbage_yields_no_exception()
    {
        var extensions = new GameDevMetadataPlugin().Descriptor.Extensions;
        var garbage = new byte[] { 0x00, 0x13, 0x37, 0x42, 0xFF, 0xFE, 0x7F, 0x00 };

        foreach (var extension in extensions)
        {
            var values = await GameDevTestHarness.ExtractAsync(extension, garbage);
            Assert.NotNull(values);
        }
    }

    [Fact]
    public void Projection_omits_absent_values_and_only_flags_invalid_documents()
    {
        var valid = GameDevValueProjection.Build(new GameDevFacts { Family = "Unit", Format = "Test" });
        var invalid = GameDevValueProjection.Build(new GameDevFacts
        {
            Family = "Unit",
            Format = "Test",
            Valid = false
        });

        Assert.Equal(2, valid.Count);
        Assert.Equal(3, invalid.Count);
        Assert.DoesNotContain(valid, value => value.Key == GameDevSchema.ScanIncomplete);
        Assert.DoesNotContain(valid, value => value.Key == GameDevSchema.HasThumbnail);
    }

    [Fact]
    public async Task List_values_are_bounded_and_joined_with_semicolons()
    {
        var facts = new GameDevFacts { Family = "Unit", Format = "Test" };
        for (var index = 0; index < 40; index++)
        {
            facts.AddNameSample("Name" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var values = GameDevValueProjection.Build(facts);
        var names = values.Single(value => value.Key == GameDevSchema.NameSamples).Text;

        Assert.NotNull(names);
        Assert.Equal(20, names!.Split("; ").Length);
        Assert.DoesNotContain("Name20", names, StringComparison.Ordinal);
    }
}
