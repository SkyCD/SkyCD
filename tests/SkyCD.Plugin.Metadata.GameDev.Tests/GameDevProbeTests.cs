using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Metadata.GameDev.Tests;

public sealed class GameDevProbeTests
{
    private const string UnityMeta = """
        fileFormatVersion: 2
        guid: 1a2b3c4d5e6f708192a3b4c5d6e7f809
        TextureImporter:
          serializedVersion: 12
          maxTextureSize: 2048
          textureType: 8
        """;

    private const string CocosScene = """
        [
          { "__type__": "cc.SceneAsset", "scene": { "__id__": 1 } },
          { "__type__": "cc.Scene", "_name": "Main", "children": [ { "__id__": 2 } ] },
          { "__type__": "cc.Node", "_name": "Camera" }
        ]
        """;

    private const string CocosMeta = """
        {
          "ver": "1.0.0",
          "importer": "cc.Scene",
          "uuid": "d1e2f3a4b5c6d7e8f90123456789abcd",
          "files": [".json", ".meta"],
          "subMetas": { "one": {}, "two": {} }
        }
        """;

    private const string CocosMaterial = """
        { "__type__": "cc.Material", "_name": "player" }
        """;

    private const string O3dePrefab = """
        {
          "ContainerEntity": {
            "Id": "Entity_[1146574390643]",
            "Name": "Root",
            "Components": { "transform": { "$type": "Component__Transform" } }
          },
          "Entities": {
            "Entity_[1146574390644]": {
              "Id": "Entity_[1146574390644]",
              "Name": "Mesh",
              "Components": { "mesh": { "$type": "Component__MeshRenderer" } }
            }
          },
          "Instances": [ { "Source": "nested.prefab" } ]
        }
        """;

    private const string O3deMaterial = """
        {
          "materialType": "Materials/Types/StandardPBR",
          "materialTypeVersion": 6,
          "propertyValues": {
            "general": { "baseColor.factor.value": [1.0, 1.0, 1.0] },
            "uv": { "uv.tiling": [1.0, 1.0] }
          }
        }
        """;

    private const string FlaxScene = """
        {
          "ID": "f1e2d3c4",
          "TypeName": "Scene",
          "EngineBuild": "1.9.0",
          "Objects": [ { "ID": "a" }, { "ID": "b" } ]
        }
        """;

    private const string SandboxScene = """
        {
          "__guid": "8f14e45fceea167a5a36dedd4bea2543",
          "__class": "Sandbox.Scene",
          "Children": [
            {
              "__guid": "c9f0f895fb98ab9159f51fd0297e236d",
              "__class": "Sandbox.GameObject",
              "Name": "Player",
              "Components": [ { "__class": "Sandbox.ModelRenderer" } ]
            }
          ]
        }
        """;

    private const string DefoldGameObject = """
        components {
          id: "script"
          component: "/main/player.script"
        }
        embedded_instances {
          id: "child"
        }
        """;

    private const string DefoldMaterial = """
        name: "player"
        vertex_program: "/builtins/materials/model.vert"
        fragment_program: "/builtins/materials/model.frag"
        samplers {
          name: "diffuse"
        }
        """;

    private const string DefoldProject = """
        [project]
        title = "Star Hopper"
        version = "1.2.3"
        publisher = "SkyCD"
        dependencies#0 = "/deps/one"
        dependencies#1 = "/deps/two"

        [display]
        width = 960
        height = 640

        [bootstrap]
        main_collection = "/main/main.collection"
        """;

    private const string CryMaterial = """
        <Material MtlName="player_mat" Shader="Illum" Version="2">
          <SubMaterials>
            <Material Name="skin" />
          </SubMaterials>
        </Material>
        """;

    private const string TiledTileset = """
        <?xml version="1.0" encoding="UTF-8"?>
        <tileset version="1.10" tiledversion="1.11.0" name="overworld" tilewidth="16" tileheight="16" tilecount="64" columns="8">
          <image source="tiles.png" width="128" height="128"/>
          <tile id="1"/>
          <tile id="2"/>
        </tileset>
        """;

    public static TheoryData<string, string> OwnDocuments => new()
    {
        { ".prefab", GameDevFixtures.UnityScene },
        { ".mat", GameDevFixtures.UnityScene },
        { ".anim", GameDevFixtures.UnityScene },
        { ".meta", UnityMeta },
        { ".scene", CocosScene },
        { ".meta", CocosMeta },
        { ".mtl", CocosMaterial },
        { ".prefab", O3dePrefab },
        { ".material", O3deMaterial },
        { ".scene", FlaxScene },
        { ".material", FlaxScene },
        { ".scene", SandboxScene },
        { ".prefab", SandboxScene },
        { ".go", DefoldGameObject },
        { ".material", DefoldMaterial },
        { ".project", DefoldProject },
        { ".mtl", CryMaterial },
        { ".tsx", TiledTileset }
    };

    public static TheoryData<string, string> ForeignDocuments => new()
    {
        { ".prefab", "import { Component } from \"sandbox\";\n" },
        { ".scene", "name: my scene\n" },
        { ".material", "just some text\n" },
        { ".mat", "newmtl steel\nKd 0.8 0.8 0.8\n" },
        { ".anim", "frames: []\n" },
        { ".meta", "guid: 1a2b3c4d5e6f708192a3b4c5d6e7f809\n" },
        { ".mtl", "newmtl steel\nKd 0.8 0.8 0.8\n" },
        { ".tsx", "export const entries = [];\n" },
        { ".go", "package main\n\nfunc main() {}\n" },
        { ".project", "<?xml version=\"1.0\"?><projectDescription name=\"app\"/>" }
    };

    [Theory]
    [MemberData(nameof(OwnDocuments))]
    public async Task Probe_accepts_documents_that_carry_their_family_marker(string extension, string text)
    {
        Assert.True(await ProbeAsync(extension, GameDevFixtures.Utf8(text)));
    }

    [Theory]
    [MemberData(nameof(ForeignDocuments))]
    public async Task Foreign_content_is_rejected_by_the_probe_and_produces_no_record(string extension, string text)
    {
        Assert.False(await ProbeAsync(extension, GameDevFixtures.Utf8(text)));
        Assert.Empty(await GameDevTestHarness.ExtractAsync(extension, text));
    }

    [Fact]
    public async Task Godot_binary_scenes_are_probed_by_their_magic()
    {
        Assert.True(await ProbeAsync(".scn", GameDevFixtures.GodotBinaryResource()));
        Assert.False(await ProbeAsync(".scn", new byte[] { 1, 2, 3, 4 }));

        var values = await GameDevTestHarness.ExtractAsync(".scn", GameDevFixtures.GodotBinaryResource());

        Assert.Equal("Godot binary scene", values.Text(GameDevSchema.Format));
        Assert.Equal("4.2.1", values.Text(GameDevSchema.EngineVersion));
    }

    [Fact]
    public async Task Unity_shared_documents_keep_their_own_formats()
    {
        var prefab = await GameDevTestHarness.ExtractAsync(".prefab", GameDevFixtures.UnityScene);
        var material = await GameDevTestHarness.ExtractAsync(".mat", GameDevFixtures.UnityScene);
        var animation = await GameDevTestHarness.ExtractAsync(".anim", GameDevFixtures.UnityScene);

        Assert.Equal("Unity prefab", prefab.Text(GameDevSchema.Format));
        Assert.Equal("Player", prefab.Text(GameDevSchema.ItemName));
        Assert.Equal("Unity material", material.Text(GameDevSchema.Format));
        Assert.Equal("Unity animation", animation.Text(GameDevSchema.Format));
    }

    [Fact]
    public async Task Unity_meta_publishes_the_guid_and_importer()
    {
        var values = await GameDevTestHarness.ExtractAsync(".meta", UnityMeta);

        Assert.Equal("Unity", values.Text(GameDevSchema.Family));
        Assert.Equal("Unity meta", values.Text(GameDevSchema.Format));
        Assert.Equal("2", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal("1a2b3c4d5e6f708192a3b4c5d6e7f809", values.Text(GameDevSchema.MetaGuid));
        Assert.Equal("TextureImporter", values.Text(GameDevSchema.ImporterType));
    }

    [Fact]
    public async Task Cocos_shared_documents_are_read_from_arrays_and_objects()
    {
        var scene = await GameDevTestHarness.ExtractAsync(".scene", CocosScene);
        Assert.Equal("Cocos", scene.Text(GameDevSchema.Family));
        Assert.Equal("Cocos scene", scene.Text(GameDevSchema.Format));
        Assert.Equal("Main", scene.Text(GameDevSchema.ItemName));
        Assert.Equal(3, scene.Integer(GameDevSchema.ClassCount));
        Assert.Contains("Camera", scene.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);

        var meta = await GameDevTestHarness.ExtractAsync(".meta", CocosMeta);
        Assert.Equal("Cocos meta", meta.Text(GameDevSchema.Format));
        Assert.Equal("1.0.0", meta.Text(GameDevSchema.FormatVersion));
        Assert.Equal("d1e2f3a4b5c6d7e8f90123456789abcd", meta.Text(GameDevSchema.MetaGuid));
        Assert.Equal("cc.Scene", meta.Text(GameDevSchema.ImporterType));
        Assert.Equal(2, meta.Integer(GameDevSchema.FileCount));
        Assert.Equal(2, meta.Integer(GameDevSchema.ResourceCount));

        var material = await GameDevTestHarness.ExtractAsync(".mtl", CocosMaterial);
        Assert.Equal("Cocos material", material.Text(GameDevSchema.Format));
        Assert.Equal("player", material.Text(GameDevSchema.ItemName));
    }

    [Fact]
    public async Task O3de_prefab_and_material_publish_entities_components_and_groups()
    {
        var prefab = await GameDevTestHarness.ExtractAsync(".prefab", O3dePrefab);
        Assert.Equal("O3DE prefab", prefab.Text(GameDevSchema.Format));
        Assert.Equal("Root", prefab.Text(GameDevSchema.ItemName));
        Assert.Equal(2, prefab.Integer(GameDevSchema.EntityCount));
        Assert.Equal(2, prefab.Integer(GameDevSchema.ObjectCount));
        Assert.Equal(2, prefab.Integer(GameDevSchema.ClassCount));
        Assert.Equal(1, prefab.Integer(GameDevSchema.InstanceCount));

        var material = await GameDevTestHarness.ExtractAsync(".material", O3deMaterial);
        Assert.Equal("O3DE material", material.Text(GameDevSchema.Format));
        Assert.Equal("StandardPBR", material.Text(GameDevSchema.ItemName));
        Assert.Equal("6", material.Text(GameDevSchema.FormatVersion));
        Assert.Equal(2, material.Integer(GameDevSchema.ObjectCount));
        Assert.Contains("general", material.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flax_shared_documents_are_typed_by_type_name_and_engine_build()
    {
        var scene = await GameDevTestHarness.ExtractAsync(".scene", FlaxScene);
        Assert.Equal("Flax scene", scene.Text(GameDevSchema.Format));
        Assert.Equal("Scene", scene.Text(GameDevSchema.ItemName));
        Assert.Equal("1.9.0", scene.Text(GameDevSchema.FormatVersion));
        Assert.Equal(3, scene.Integer(GameDevSchema.NodeCount));

        var material = await GameDevTestHarness.ExtractAsync(".material", FlaxScene);
        Assert.Equal("Flax material", material.Text(GameDevSchema.Format));
    }

    [Fact]
    public async Task Sandbox_shared_documents_count_game_objects_and_components()
    {
        var scene = await GameDevTestHarness.ExtractAsync(".scene", SandboxScene);

        Assert.Equal("s&box scene", scene.Text(GameDevSchema.Format));
        Assert.Equal(2, scene.Integer(GameDevSchema.EntityCount));
        Assert.Equal(1, scene.Integer(GameDevSchema.ObjectCount));
        Assert.Equal(3, scene.Integer(GameDevSchema.ClassCount));
        Assert.Contains("Player", scene.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Defold_shared_documents_read_game_objects_materials_and_projects()
    {
        var gameObject = await GameDevTestHarness.ExtractAsync(".go", DefoldGameObject);
        Assert.Equal("Defold game object", gameObject.Text(GameDevSchema.Format));
        Assert.Equal(2, gameObject.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(1, gameObject.Integer(GameDevSchema.BlockCount));

        var material = await GameDevTestHarness.ExtractAsync(".material", DefoldMaterial);
        Assert.Equal("Defold material", material.Text(GameDevSchema.Format));
        Assert.Equal("player", material.Text(GameDevSchema.ItemName));
        Assert.Equal(2, material.Integer(GameDevSchema.ScriptCount));
        Assert.Equal(1, material.Integer(GameDevSchema.ResourceCount));

        var project = await GameDevTestHarness.ExtractAsync(".project", DefoldProject);
        Assert.Equal("Defold project", project.Text(GameDevSchema.Format));
        Assert.Equal("Star Hopper", project.Text(GameDevSchema.ProjectName));
        Assert.Equal("1.2.3", project.Text(GameDevSchema.FormatVersion));
        Assert.Equal(2, project.Integer(GameDevSchema.ResourceCount));
    }

    [Fact]
    public async Task Cryengine_material_reads_name_shader_and_submaterials()
    {
        var values = await GameDevTestHarness.ExtractAsync(".mtl", CryMaterial);

        Assert.Equal("CRYENGINE material", values.Text(GameDevSchema.Format));
        Assert.Equal("player_mat", values.Text(GameDevSchema.ItemName));
        Assert.Equal("CRYENGINE Illum", values.Text(GameDevSchema.Application));
        Assert.Equal(1, values.Integer(GameDevSchema.ResourceCount));
        Assert.Contains("skin", values.Text(GameDevSchema.NameSamples), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tiled_tilesets_are_read_from_the_shared_tsx_extension()
    {
        var values = await GameDevTestHarness.ExtractAsync(".tsx", TiledTileset);

        Assert.Equal("Tiled", values.Text(GameDevSchema.Family));
        Assert.Equal("Tiled tileset", values.Text(GameDevSchema.Format));
        Assert.Equal("overworld", values.Text(GameDevSchema.ItemName));
        Assert.Equal("1.11.0", values.Text(GameDevSchema.FormatVersion));
        Assert.Equal(128, values.Integer(GameDevSchema.ImageWidth));
        Assert.Equal(64, values.Integer(GameDevSchema.ResourceCount));
        Assert.Equal(2, values.Integer(GameDevSchema.ObjectCount));
    }

    private static async Task<bool> ProbeAsync(string extension, byte[] data) =>
        await new GameDevMetadataPlugin().CanHandleAsync(
            new MediaItem(extension, null, data.Length),
            new ByteArrayMediaSource(data),
            CancellationToken.None);
}
