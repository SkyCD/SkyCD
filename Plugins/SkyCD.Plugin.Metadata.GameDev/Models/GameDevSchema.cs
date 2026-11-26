using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Models;

/// <summary>
/// Stable schema identities, field keys and claimed extensions for game engine project and asset facts.
/// Field keys and units are part of the cached-data contract: changing the meaning or unit of a field
/// requires a new schema version, because the host keys stored results by schema id and version.
/// </summary>
/// <remarks>
/// Extensions that another plugin also claims are deliberately absent until the shared format probe
/// (#1258) exists. Claiming <c>.prefab</c>, <c>.scene</c>, <c>.material</c>, <c>.mat</c>, <c>.mtl</c>,
/// <c>.scn</c>, <c>.meta</c>, <c>.anim</c>, <c>.tsx</c>, <c>.go</c> or <c>.project</c> without a byte-level
/// probe would either steal files from another provider or publish empty records for foreign content, so
/// those rows stay unclaimed and are documented in <c>docs/plugins/gamedev.md</c>.
/// </remarks>
public static class GameDevSchema
{
    public const string MetadataSchemaId = "skycd.gamedev.metadata";
    public const int MetadataSchemaVersion = 1;

    public const string PreviewSchemaId = "skycd.gamedev.preview";
    public const int PreviewSchemaVersion = 1;

    public const string Family = "Family";
    public const string Format = "Format";
    public const string FormatVersion = "FormatVersion";
    public const string EngineVersion = "EngineVersion";
    public const string Application = "Application";
    public const string ProjectName = "ProjectName";
    public const string ItemName = "ItemName";
    public const string Compiled = "Compiled";
    public const string Cooked = "Cooked";
    public const string Valid = "Valid";
    public const string ScanIncomplete = "ScanIncomplete";
    public const string NodeCount = "NodeCount";
    public const string EntityCount = "EntityCount";
    public const string InstanceCount = "InstanceCount";
    public const string ObjectCount = "ObjectCount";
    public const string SceneCount = "SceneCount";
    public const string LevelCount = "LevelCount";
    public const string LayerCount = "LayerCount";
    public const string TileLayerCount = "TileLayerCount";
    public const string ObjectGroupCount = "ObjectGroupCount";
    public const string FrameCount = "FrameCount";
    public const string TagCount = "TagCount";
    public const string AssetCount = "AssetCount";
    public const string ResourceCount = "ResourceCount";
    public const string ImportCount = "ImportCount";
    public const string ExportCount = "ExportCount";
    public const string FileCount = "FileCount";
    public const string ScriptCount = "ScriptCount";
    public const string LabelCount = "LabelCount";
    public const string CharacterCount = "CharacterCount";
    public const string TrackCount = "TrackCount";
    public const string ClipCount = "ClipCount";
    public const string ChunkCount = "ChunkCount";
    public const string BlockCount = "BlockCount";
    public const string ClassCount = "ClassCount";
    public const string ImageWidth = "ImageWidth";
    public const string ImageHeight = "ImageHeight";
    public const string MapWidth = "MapWidth";
    public const string MapHeight = "MapHeight";
    public const string TileWidth = "TileWidth";
    public const string TileHeight = "TileHeight";
    public const string ColorMode = "ColorMode";
    public const string ColorDepth = "ColorDepth";
    public const string TotalDuration = "TotalDuration";
    public const string Loop = "Loop";
    public const string HasThumbnail = "HasThumbnail";
    public const string AnimationNames = "AnimationNames";
    public const string NameSamples = "NameSamples";
    public const string TilesetNames = "TilesetNames";

    /// <summary>Extensions this plugin claims today. See the remarks for the probe-blocked rows.</summary>
    public static readonly IReadOnlyList<string> Extensions =
    [
        // Aseprite
        ".aseprite", ".ase",
        // Godot text and binary resources
        ".tscn", ".escn", ".tres", ".import", ".gdextension", ".godot", ".res", ".pck",
        // Unity (the shared .prefab/.mat/.anim/.meta rows wait for #1258)
        ".unity", ".asset", ".controller", ".overrideController", ".physicMaterial", ".unitypackage",
        // Tiled and LDtk (.tsx waits for #1258 because Metadata.Text claims it for TypeScript)
        ".tmx", ".tmj", ".tsj", ".world", ".ldtk", ".ldtkl",
        // Unreal and UEFN
        ".uproject", ".uplugin", ".uasset", ".umap", ".verse",
        // Roblox Studio
        ".rbxl", ".rbxm", ".rbxlx", ".rbxmx",
        // GameMaker
        ".yyp", ".yy", ".yyz",
        // RPG Maker MV/MZ and XP/VX/VX Ace
        ".rpgproject", ".rmmzproject", ".rxdata", ".rvdata", ".rvdata2",
        // Construct 3
        ".c3p",
        // Cocos (the shared .scene/.prefab/.anim/.meta rows wait for #1258)
        ".fire", ".csd",
        // Defold (the shared .project/.go/.material rows wait for #1258)
        ".collection", ".gui", ".tilemap", ".atlas", ".sprite",
        // CRYENGINE (the shared .mtl row waits for #1258)
        ".cryproject", ".cryproj", ".chrparams",
        // O3DE (the shared .prefab/.material rows wait for #1258)
        ".slice", ".azasset",
        // Flax (the shared .scene/.prefab/.material rows wait for #1258)
        ".flaxproj",
        // Stride
        ".sdscene", ".sdprefab", ".sdmat", ".sdtex",
        // Wicked Engine
        ".wiscene",
        // Ren'Py
        ".rpy", ".rpym", ".rpyc", ".rpa",
        // s&box (the shared .scene/.prefab rows wait for #1258)
        ".sbproj",
        // MonoGame and FNA
        ".mgcb", ".xnb",
        // LÖVE
        ".love"
    ];

    /// <summary>None of these project containers has a registered media type; selection is by extension.</summary>
    public static readonly IReadOnlyList<string> ContentTypes = [];

    /// <summary>Sources with these extension roots produce an on-demand structure page.</summary>
    public static readonly IReadOnlyList<string> StructureExtensions =
    [
        ".tscn", ".escn", ".tres", ".godot", ".unity",
        ".tmx", ".tmj", ".ldtk", ".rbxlx", ".rbxmx",
        ".slice", ".sdscene", ".sdprefab", ".csd",
        ".collection", ".gui", ".rpy", ".yyp", ".c3p", ".uproject"
    ];

    public static readonly IReadOnlyList<string> AsepriteExtensions = [".aseprite", ".ase"];
    public static readonly IReadOnlyList<string> UnityPackageExtensions = [".unitypackage"];
    public static readonly IReadOnlyList<string> UnrealExtensions = [".uasset", ".umap"];
    public static readonly IReadOnlyList<string> WickedExtensions = [".wiscene"];
    public static readonly IReadOnlyList<string> XnbExtensions = [".xnb"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new(GameDevSchema.Family, MediaMetadataType.Text),
        new(GameDevSchema.Format, MediaMetadataType.Text),
        new(GameDevSchema.FormatVersion, MediaMetadataType.Text),
        new(GameDevSchema.EngineVersion, MediaMetadataType.Text),
        new(GameDevSchema.Application, MediaMetadataType.Text),
        new(GameDevSchema.ProjectName, MediaMetadataType.Text),
        new(GameDevSchema.ItemName, MediaMetadataType.Text),
        new(GameDevSchema.Compiled, MediaMetadataType.Boolean),
        new(GameDevSchema.Cooked, MediaMetadataType.Boolean),
        new(GameDevSchema.Valid, MediaMetadataType.Boolean),
        new(GameDevSchema.ScanIncomplete, MediaMetadataType.Boolean),
        new(GameDevSchema.NodeCount, MediaMetadataType.Integer),
        new(GameDevSchema.EntityCount, MediaMetadataType.Integer),
        new(GameDevSchema.InstanceCount, MediaMetadataType.Integer),
        new(GameDevSchema.ObjectCount, MediaMetadataType.Integer),
        new(GameDevSchema.SceneCount, MediaMetadataType.Integer),
        new(GameDevSchema.LevelCount, MediaMetadataType.Integer),
        new(GameDevSchema.LayerCount, MediaMetadataType.Integer),
        new(GameDevSchema.TileLayerCount, MediaMetadataType.Integer),
        new(GameDevSchema.ObjectGroupCount, MediaMetadataType.Integer),
        new(GameDevSchema.FrameCount, MediaMetadataType.Integer),
        new(GameDevSchema.TagCount, MediaMetadataType.Integer),
        new(GameDevSchema.AssetCount, MediaMetadataType.Integer),
        new(GameDevSchema.ResourceCount, MediaMetadataType.Integer),
        new(GameDevSchema.ImportCount, MediaMetadataType.Integer),
        new(GameDevSchema.ExportCount, MediaMetadataType.Integer),
        new(GameDevSchema.FileCount, MediaMetadataType.Integer),
        new(GameDevSchema.ScriptCount, MediaMetadataType.Integer),
        new(GameDevSchema.LabelCount, MediaMetadataType.Integer),
        new(GameDevSchema.CharacterCount, MediaMetadataType.Integer),
        new(GameDevSchema.TrackCount, MediaMetadataType.Integer),
        new(GameDevSchema.ClipCount, MediaMetadataType.Integer),
        new(GameDevSchema.ChunkCount, MediaMetadataType.Integer),
        new(GameDevSchema.BlockCount, MediaMetadataType.Integer),
        new(GameDevSchema.ClassCount, MediaMetadataType.Integer),
        new(GameDevSchema.ImageWidth, MediaMetadataType.Integer, "px"),
        new(GameDevSchema.ImageHeight, MediaMetadataType.Integer, "px"),
        new(GameDevSchema.MapWidth, MediaMetadataType.Integer),
        new(GameDevSchema.MapHeight, MediaMetadataType.Integer),
        new(GameDevSchema.TileWidth, MediaMetadataType.Integer, "px"),
        new(GameDevSchema.TileHeight, MediaMetadataType.Integer, "px"),
        new(GameDevSchema.ColorMode, MediaMetadataType.Text),
        new(GameDevSchema.ColorDepth, MediaMetadataType.Integer, "bit"),
        new(GameDevSchema.TotalDuration, MediaMetadataType.Integer, "ms"),
        new(GameDevSchema.Loop, MediaMetadataType.Boolean),
        new(GameDevSchema.HasThumbnail, MediaMetadataType.Boolean),
        new(GameDevSchema.AnimationNames, MediaMetadataType.Text),
        new(GameDevSchema.NameSamples, MediaMetadataType.Text),
        new(GameDevSchema.TilesetNames, MediaMetadataType.Text)
    ]);

    /// <summary>Preview results carry artifacts rather than values, so the schema is identity only.</summary>
    public static MediaMetadataSchema Preview { get; } = new(PreviewSchemaId, PreviewSchemaVersion, []);
}
