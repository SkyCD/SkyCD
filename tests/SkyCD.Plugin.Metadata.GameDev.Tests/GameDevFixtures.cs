using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;
using SkiaSharp;

namespace SkyCD.Plugin.Metadata.GameDev.Tests;

/// <summary>
/// Builds minimal synthetic sources for every claimed family. Each fixture is the smallest file the reader
/// accepts, so a test failure points at the reader rather than at an oversized sample.
/// </summary>
internal static class GameDevFixtures
{
    internal static byte[] Utf8(string text) =>
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    internal static string GodotProject => """
        ; Engine configuration file.
        config_version=5

        [application]

        config/name="Star Hopper"
        config/features=PackedStringArray("4.2", "GL Compatibility")
        run/main_scene="res://scenes/main.tscn"

        [rendering]

        renderer/rendering_method="mobile"

        [autoload]

        Game="*res://autoload/game.gd"

        [input]

        jump={
        "deadzone": 0.5
        }
        """;

    internal static string GodotScene => """
        [gd_scene load_steps=3 format=3 uid="uid://c0ffee"]

        [ext_resource type="Script" path="res://player.gd" id="1"]

        [sub_resource type="RectangleShape2D" id="RectangleShape2D_1"]

        [node name="Main" type="Node2D"]

        [node name="Player" type="CharacterBody2D" parent="."]

        [connection signal="pressed" from="Player" to="." method="_on_pressed"]
        """;

    internal static string GodotResource => """
        [gd_resource type="Animation" format=3 script_class="Tween"]

        [ext_resource type="Script" path="res://anim.gd" id="1"]
        """;

    internal static string GodotImport => """
        [remap]

        importer="texture"
        type="CompressedTexture2D"
        uid="uid://import1"

        [deps]

        source_file="res://icon.svg"
        dest_files=["res://.godot/imported/icon.svg-1.ctex"]
        """;

    internal static string GodotExtension => """
        [configuration]

        entry_symbol = "example_library_init"
        compatibility_minimum = "4.1"

        [libraries]

        windows.debug.x86_64 = "res://bin/windows.dll"

        [dependencies]

        windows.debug.x86_64 = { "smoke": [] }
        """;

    internal static byte[] GodotBinaryResource()
    {
        var data = new byte[24];
        Encoding.ASCII.GetBytes("RSRC").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 4);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 1);
        return data;
    }

    internal static byte[] GodotPack()
    {
        var data = new byte[24];
        Encoding.ASCII.GetBytes("GDPC").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), 1);
        return data;
    }

    internal static string UnityScene => """
        %YAML 1.1
        %TAG !u! tag:unity3d.com,2011:
        --- !u!29 &1
        OcclusionCullingSettings:
          m_ObjectHideFlags: 0
        --- !u!1 &100
        GameObject:
          m_Name: Player
          m_Component:
          - component: {fileID: 400}
          - component: {fileID: 11400000}
        --- !u!4 &400
        Transform:
          m_GameObject: {fileID: 100}
        --- !u!114 &11400000
        MonoBehaviour:
          m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef, type: 3}
        --- !u!1 &200
        GameObject:
          m_Name: Ground
        """;

    internal static string TiledMap => """
        <?xml version="1.0" encoding="UTF-8"?>
        <map version="1.10" tiledversion="1.11.0" orientation="orthogonal" width="40" height="30"
             tilewidth="16" tileheight="16">
          <tileset firstgid="1" name="tiles" tilewidth="16" tileheight="16" tilecount="64" columns="8"/>
          <layer id="1" name="Ground" width="40" height="30">
            <data encoding="csv">0,0,0</data>
          </layer>
          <objectgroup id="2" name="Objects">
            <object id="1" name="Spawn" x="16" y="32"/>
            <object id="2" name="Exit" x="48" y="64"/>
          </objectgroup>
          <properties>
            <property name="weather" value="rain"/>
          </properties>
        </map>
        """;

    internal static string TiledWorld => """
        <?xml version="1.0" encoding="UTF-8"?>
        <world version="1.0" tiledversion="1.11.0">
          <map filename="level1.tmx" x="0" y="0" width="40" height="30"/>
          <map filename="level2.tmx" x="40" y="0" width="40" height="30"/>
        </world>
        """;

    internal static string TiledJsonMap => """
        {
          "tiledversion": "1.11.0",
          "width": 20,
          "height": 10,
          "tilewidth": 8,
          "tileheight": 8,
          "layers": [
            { "type": "tilelayer", "name": "Ground" },
            { "type": "objectgroup", "name": "Spawns", "objects": [ { "name": "a" }, { "name": "b" } ] }
          ],
          "tilesets": [ { "name": "overworld" } ]
        }
        """;

    internal static string LdtkProject => """
        {
          "jsonVersion": "1.5.3",
          "appVersion": "1.5.3",
          "name": "Dungeon",
          "levels": [ { "identifier": "Level_0" }, { "identifier": "Level_1" } ],
          "worlds": [ { "identifier": "World" } ],
          "defs": { "entities": [ { "identifier": "Player" } ], "tilesets": [ { "identifier": "Tiles" } ] }
        }
        """;

    internal static string UnrealProject => """
        {
          // Unreal project descriptor
          "FileVersion": 3,
          "EngineAssociation": "5.4",
          "Category": "",
          "Description": "",
          "Modules": [
            { "Name": "StarHopper", "Type": "Runtime" }
          ],
          "Plugins": [
            { "Name": "ModelingToolsEditorMode", "Enabled": true }
          ],
          "TargetPlatforms": [ "Windows", "Linux" ],
        }
        """;

    internal static string UnrealVerse => """
        using { /Fortnite.com/Devices }

        star_hopper := class(creative_device):
            OnBegin<override>()<suspends>:void=
                Print("Start")
        """;

    internal static byte[] XnbTexture(int width = 2, int height = 2)
    {
        const string reader = "Microsoft.Xna.Framework.Content.Texture2DReader";
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)'X');
        writer.Write((byte)'N');
        writer.Write((byte)'B');
        writer.Write((byte)'w');
        writer.Write((byte)5);
        writer.Write((byte)0);
        writer.Write(0); // patched below
        writer.Write7BitEncodedInt(reader.Length);
        writer.Write(Encoding.ASCII.GetBytes(reader));
        writer.Write(0);
        writer.Write(0); // Color
        writer.Write(width);
        writer.Write(height);
        writer.Write(1);
        writer.Write(width * height * 4);
        for (var index = 0; index < width * height; index++)
        {
            writer.Write((byte)0xFF);
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
            writer.Write((byte)0xFF);
        }

        writer.Flush();
        var data = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(6), data.Length);
        return data;
    }

    internal static byte[] XnbCompressed()
    {
        var data = new byte[16];
        Encoding.ASCII.GetBytes("XNB").CopyTo(data, 0);
        data[3] = (byte)'w';
        data[4] = 5;
        data[5] = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(6), data.Length);
        return data;
    }

    internal static byte[] UnrealPackage(string name = "BP_Player", byte[]? thumbnail = null)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0x9E2A83C1u);
        writer.Write(-4);
        writer.Write(0); // custom versions
        writer.Write(16); // total header size check only
        WriteFString(writer, name);
        writer.Write(0x20000000u); // cooked
        writer.Write(3); // name count
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(2); // export count
        writer.Write(0);
        writer.Write(1); // import count
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        var tablePosition = (int)stream.Position;
        writer.Write(0); // thumbnail table offset, patched below

        if (thumbnail is not null)
        {
            writer.Flush();
            var tableOffset = (int)stream.Position;
            writer.Write(1);
            WriteFString(writer, "Default");
            var imagePositionField = (int)stream.Position;
            writer.Write(0);
            writer.Write(0);
            writer.Flush();
            var imagePosition = (int)stream.Position;
            writer.Write(thumbnail);
            var end = (int)stream.Position;
            stream.Position = imagePositionField;
            writer.Write(imagePosition);
            writer.Write(thumbnail.Length);
            stream.Position = tablePosition;
            writer.Write(tableOffset);
            stream.Position = end;
        }

        writer.Flush();
        return stream.ToArray();
    }

    internal static byte[] WickedScene(byte[]? thumbnail = null)
    {
        var length = thumbnail?.Length ?? 0;
        var data = new byte[16 + length];
        BinaryPrimitives.WriteUInt64LittleEndian(data, 1);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(8), (ulong)length);
        thumbnail?.CopyTo(data, 16);
        return data;
    }

    internal static byte[] FakeJpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9];

    internal static byte[] PngPreview(int width = 8, int height = 8)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.CornflowerBlue);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static byte[] UnityPackage(byte[] previewPng, string assetPath = "Assets/Hero/prefab")
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            AddTarEntry(writer, assetPath + "/pathname", Utf8("Assets/Hero/prefab.prefab"));
            AddTarEntry(writer, assetPath + "/asset", [0x00]);
            AddTarEntry(writer, assetPath + "/preview.png", previewPng);
        }

        return memory.ToArray();
    }

    internal static byte[] Zip(IReadOnlyList<(string Name, byte[] Content)> entries)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
                using var stream = entry.Open();
                stream.Write(content);
            }
        }

        return memory.ToArray();
    }

    internal static byte[] LoveArchive => Zip(
    [
        ("main.lua", Utf8("function love.draw() end")),
        ("conf.lua", Utf8("function love.conf(t) t.version = \"11.4\" end"))
    ]);

    internal static byte[] ConstructProject => Zip(
    [
        ("project.c3proj", Utf8("""
            {
              "projectFormatVersion": "1.9",
              "savedWithRelease": 41400,
              "layouts": [ { "name": "Main" }, { "name": "Menu" } ],
              "eventSheets": [ { "name": "Events" } ],
              "scripts": [ { "name": "utils.js" } ]
            }
            """))
    ]);

    internal static byte[] GameMakerArchive => Zip(
    [
        ("StarHopper.yyp", Utf8("""
            {
              "name": "StarHopper",
              "IDEVersion": "2024.6.2.162",
              "resourceVersion": "2.0",
              "resources": [ { "id": { "name": "obj_player", "path": "objects/obj_player/obj_player.yy" } } ],
              "RoomOrderNodes": [ { "roomId": { "name": "rm_title", "path": "rooms/rm_title/rm_title.yy" } } ],
              "Folders": [ { "folderPath": "objects/" } ],
              "AudioGroups": [ { "name": "audiogroup_default" } ],
              "TextureGroups": [ { "name": "Default" } ]
            }
            """)),
        ("objects/obj_player/obj_player.yy", Utf8("{}"))
    ]);

    internal static string RobloxPlace => """
        <roblox version="4">
          <Item class="Workspace" referent="0">
            <Properties>
              <string name="Name">Workspace</string>
            </Properties>
            <Item class="Part" referent="1">
              <Properties>
                <string name="Name">Baseplate</string>
              </Properties>
            </Item>
            <Item class="Script" referent="2">
              <Properties>
                <string name="Name">Main</string>
              </Properties>
            </Item>
          </Item>
        </roblox>
        """;

    internal static byte[] RobloxBinary(uint classCount = 4, uint instanceCount = 12)
    {
        var data = new byte[32];
        ReadOnlySpan<byte> magic = [0x3C, 0x72, 0x6F, 0x62, 0x6C, 0x6F, 0x78, 0x21, 0x89, 0xFF, 0x0D, 0x0A, 0x1A, 0x0A];
        magic.CopyTo(data);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16), classCount);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(20), instanceCount);
        return data;
    }

    internal static string RenpyScript => """
        define e = Character("Eileen")

        label start:
            e "Hello, world."
            jump chapter_two

        label chapter_two:
            return
        """;

    internal static byte[] RenpyArchive()
    {
        var header = Utf8("RPA-3.0 0000000010deadbeef\n");
        var data = new byte[192];
        header.CopyTo(data, 0);
        return data;
    }

    internal static string RpgMakerProject => """
        RPGMV 1.6.1
        {"name":"Star Hopper","rpgmvVersion":"1.6.1"}
        """;

    internal static string StrideScene => """
        !Game.Scene,Stride.Engine
        SerializerVersion: 2.0.0.0
        Id: 12345678-1234-1234-1234-123456789012
        Hierarchy:
            RootParts:
                - !Game.Player,Game
                  Name: Player
                - !Game.Camera,Game
                  Name: Main Camera
        """;

    internal static string MonoGameContentProject => """
        #----------------------------- Global Properties ----------------------------#
        /outputDir:bin/$(Platform)
        /intermediateDir:obj
        /platform:DesktopGL
        /config:
        /profile:Reach

        #-------------------------------- References --------------------------------#
        /reference:..\Shared\SharedContent.mgcb

        #---------------------------------- Content ---------------------------------#
        #begin Textures/hero.png
        /importer:TextureImporter
        /processor:TextureProcessor
        /compile:Textures/hero.png
        /build:Textures/hero.png
        /copy:Content/copy.txt
        """;

    internal static string AtlasText => """
        hero.png
          format: RGBA8888
          filter: Linear,Linear
          size: 1024,512
        hero_idle
          bounds: 0,0,64,64
        hero_run
          bounds: 64,0,64,64
        """;

    private static void AddTarEntry(TarWriter writer, string name, byte[] content)
    {
        var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
        {
            DataStream = new MemoryStream(content, writable: false)
        };
        writer.WriteEntry(entry);
    }

    private static void WriteFString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value + "\0");
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
