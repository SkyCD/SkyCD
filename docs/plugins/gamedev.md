# Game engine project and asset metadata

Implements #1251 on the media capability contracts from [media-capabilities.md](media-capabilities.md).

Assembly: `Plugins/SkyCD.Plugin.Metadata.GameDev/` → plugin id `SkyCD.Plugin.GameDev`.
Tests: `tests/SkyCD.Plugin.Metadata.GameDev.Tests/`.

## What it provides

| Capability | Type | Selection | Schema |
|---|---|---|---|
| Typed project/asset facts | `GameDevMetadataPlugin : IMediaMetadataCapability` | priority 10, indexing pass | `skycd.gamedev.metadata` v1 |
| On-demand structure page | `GameDevStructurePreviewPlugin : IMediaPreviewCapability` | priority 30, explicit request | `skycd.gamedev.preview` v1 |
| Aseprite frame 0 | `GameDevAsepritePreviewPlugin` | index-time pixel preview | `skycd.gamedev.preview` v1 |
| Unity package thumbnail | `GameDevUnityPackagePreviewPlugin` | index-time pass-through | `skycd.gamedev.preview` v1 |
| Unreal package thumbnail | `GameDevUnrealPreviewPlugin` | index-time pass-through | `skycd.gamedev.preview` v1 |
| Wicked Engine scene thumbnail | `GameDevWickedScenePreviewPlugin` | index-time pass-through | `skycd.gamedev.preview` v1 |
| MonoGame/FNA XNB texture | `GameDevXnbPreviewPlugin` | index-time pixel preview | `skycd.gamedev.preview` v1 |

Discovery uses assembly metadata and implemented capability types only; there is no `plugin.json`.

## Coverage

| Engine / tool | Extensions | Family | Facts read |
|---|---|---|---|
| Aseprite | `.aseprite`, `.ase` | Aseprite | canvas, frames, layers, tags with loop flag, slices, tilesets, frame duration, color depth; frame 0 thumbnail |
| Godot | `.tscn`, `.escn`, `.tres`, `.import`, `.gdextension`, `.godot`, `.res`, `.pck` | Godot | project name/version/renderer, scene format/uid, node and class counts, scripts, resources, connections, binary header version |
| Unity | `.unity`, `.asset`, `.controller`, `.overrideController`, `.physicMaterial`, `.unitypackage` | Unity | YAML class anchors, GameObjects, transforms, script and GUID counts, `m_Name` samples; package asset and preview counts |
| Unreal Engine / UEFN | `.uproject`, `.uplugin`, `.uasset`, `.umap`, `.verse` | Unreal | project modules/plugins/platforms, package version words, cooked flag, name/import/export counts, stored thumbnail; Verse file/function/class counts |
| Roblox Studio | `.rbxl`, `.rbxm`, `.rbxlx`, `.rbxmx` | Roblox | binary class/instance counts and version; XML item/class histogram and place name |
| GameMaker | `.yyp`, `.yy`, `.yyz` | GameMaker | project name, IDE and resource versions, resource/room/folder/audio/texture group counts; archive entry count merged with the embedded project |
| RPG Maker MV/MZ, XP/VX/VX Ace | `.rpgproject`, `.rmmzproject`, `.rxdata`, `.rvdata`, `.rvdata2` | RPG Maker | project marker and version; bounded Ruby Marshal symbol/string/object counts |
| Construct 3 | `.c3p` | Construct | `project.c3proj` format version, release, layout/event-sheet/script counts |
| Cocos | `.fire`, `.csd` | Cocos | Creator `__type__` tree and `_name` samples; Studio XML version and node names |
| Defold | `.collection`, `.gui`, `.tilemap`, `.atlas`, `.sprite` | Defold | protobuf-text resource ids and type histogram |
| CRYENGINE | `.cryproject`, `.cryproj`, `.chrparams` | CRYENGINE | project name/version/required modules; character-parameter bone names |
| O3DE | `.slice`, `.azasset` | O3DE | slice root/version and class instances; asset `$type` and serialization version |
| Flax | `.flaxproj` | Flax | name, version, target, default scene |
| Stride | `.sdscene`, `.sdprefab`, `.sdmat`, `.sdtex` | Stride | first type tag, serializer version, id, tagged class and entity names |
| Wicked Engine | `.wiscene` | Wicked Engine | header version and embedded JPEG thumbnail flag |
| Ren'Py | `.rpy`, `.rpym`, `.rpyc`, `.rpa` | Ren'Py | label/character/line counts; compiled-script slot table; archive version and index offset |
| s&box | `.sbproj` | s&box | title, project type, startup scene, resource and code counts |
| MonoGame / FNA | `.mgcb`, `.xnb` | MonoGame | content directives and platform/profile; XNB platform, version, reader, texture surface |
| LÖVE | `.love` | LÖVE | archive entry count, `main.lua`/`conf.lua` presence, `t.version` |
| libGDX / Phaser | `.atlas` | libGDX | pages, regions, key block format/filter/size; a Defold-style atlas stays with Defold |
| Tiled | `.tmx`, `.tmj`, `.tsj`, `.world` | Tiled | map/tile sizes, layer and object-group counts, objects, tilesets, properties, world map list |
| LDtk | `.ldtk`, `.ldtkl` | LDtk | JSON/app versions, project name, levels, worlds, entity/tileset definitions, layer instances |

Extensions are matched exactly and case-insensitively; every provider whose descriptor matches runs. A file
whose bytes do not match the extension's signature publishes nothing (or `Valid=false` when the family is
recognized but damaged), so a renamed file never inherits another engine's facts.

### Probe-blocked rows (shared extensions, #1258)

Until the shared media-format probe lands, this plugin does **not** claim extensions that another provider
already claims or that several engines share without a reliable byte signature:

| Extension | Other claim | Revisit |
|---|---|---|
| `.prefab`, `.scene`, `.material`, `.mat`, `.scn`, `.anim`, `.meta` | Unity/Cocos/Flax/O3DE/Godot variants | needs probe (#1258) |
| `.mtl` | Wavefront material (archives/metadata) | needs probe |
| `.tsx` | TypeScript source in `SkyCD.Plugin.Metadata.Text` | needs probe |
| `.go` | Go source in `SkyCD.Plugin.Metadata.Text` | needs probe |
| `.project` | various IDE/project containers | needs probe |

Claiming these without a probe would either steal files from another provider or publish empty records for
foreign content, so they stay unclaimed and are covered here instead.

Unclaimable today (no stable signature or no public format): PlayCanvas, GDevelop, Solar2D, Heaps,
HaxeFlixel and UPBGE. Their projects remain plain files or archives.

## Metadata schema

`skycd.gamedev.metadata`, version 1. Field keys and units are part of the cached-data contract; changing a
meaning or unit requires a new schema version.

| Group | Keys |
|---|---|
| Identity | `Family`, `Format`, `FormatVersion`, `EngineVersion`, `Application`, `ProjectName`, `ItemName` |
| State | `Compiled`, `Cooked`, `Valid` (published only as `false`), `ScanIncomplete` (only when `true`), `HasThumbnail` (only when `true`) |
| Counts | `NodeCount`, `EntityCount`, `InstanceCount`, `ObjectCount`, `SceneCount`, `LevelCount`, `LayerCount`, `TileLayerCount`, `ObjectGroupCount`, `FrameCount`, `TagCount`, `AssetCount`, `ResourceCount`, `ImportCount`, `ExportCount`, `FileCount`, `ScriptCount`, `LabelCount`, `CharacterCount`, `TrackCount`, `ClipCount`, `ChunkCount`, `BlockCount`, `ClassCount` |
| Sizes | `ImageWidth` (`px`), `ImageHeight` (`px`), `MapWidth`, `MapHeight`, `TileWidth` (`px`), `TileHeight` (`px`) |
| Media | `ColorMode`, `ColorDepth` (`bit`), `TotalDuration` (`ms`), `Loop` |
| Lists | `AnimationNames`, `NameSamples`, `TilesetNames` — semicolon-joined, at most 20 entries of 120 characters |

Absent values are omitted rather than published as zero or an empty string. A reader that could not see a
section therefore never claims a count for it, and `ScanIncomplete` is the one flag that says the prefix
bound cut the document.

## Previews

- **Pixel previews** are generated during indexing, because the catalog list shows them. Aseprite flattens
  frame 0 with visible layers and tilemap cels; Unity and Unreal pass their stored image through unchanged
  unless it exceeds 512 px on the long edge; Wicked passes its JPEG through; XNB decodes uncompressed
  Texture2D surfaces only. PNG is kept when the image carries alpha, JPEG otherwise.
- **Structure pages** are on-demand and draw the family, format, versions, sizes, counts and up to 64
  collected names on the shared A-series page (`SkiaPageRenderer`, 512 px). They duplicate facts the metadata
  extraction already stores, so indexing does not pay for them.
- A missing rasterizer, a damaged thumbnail or a format without pixels returns no artifact; metadata stays
  intact. No preview ever opens a referenced file.

## Bounds and safety

- Text families read a 4 MiB prefix; whole-document formats (Aseprite, Unity package, UPackage, XNB,
  wiscene) are capped at 16 MiB; binary headers read only what they need (Godot `.res`/`.pck` 64 KiB,
  Roblox 64 bytes, RPA 192 bytes).
- ZIP and tar containers are walked from their directory with at most 4 096 entries; one entry is capped at
  8 MiB and the total inflated bytes at 8 MiB. Nothing is extracted, imported or written, and compressed
  Roblox/XNB payloads are never decompressed.
- XML uses `DtdProcessing.Prohibit` with a null resolver; JSON uses a depth cap and, where the engine itself
  allows them, comments and trailing commas. A DTD-bearing or malformed document publishes nothing.
- Ruby Marshal data, protobuf text and binary headers are walked by hand within fixed byte and depth bounds;
  the Marshal walker never reconstructs an object graph or resolves a class.
- The plugin holds bounded bytes from `IMediaSource` and never a path, so a referenced tileset, script, GUID
  target, texture or media file cannot be opened even by mistake. There is no network or filesystem access.
- Damaged or foreign content produces no exception: the reader returns `null` or a `Valid=false` record and
  the scan continues.

## Dependencies

- **AsepriteDotNet 1.9.1** (MIT) — decodes the Aseprite file model and flattens frames. Its writing and
  painting APIs are unused.
- **SkiaSharp 3.119.4** (MIT) with Linux, macOS and Win32 native assets — downsamples and encodes thumbnails;
  the structure page reuses the shared preview renderer.

Everything else uses the .NET 10 base class library (`System.Text.Json`, `System.Xml`, `ZipArchive`,
`GZipStream`, `System.Formats.Tar`). No package adds an incompatible license to the Trivy gate in
[dependency-policy.md](../legal/dependency-policy.md).

## Known limits

- A `.atlas` file is routed by content: a `.png`/`.jpg` page with a `format:`/`size:` key block is libGDX,
  anything else is Defold.
- Compressed XNB and compressed Roblox chunks publish header facts only; decoding them needs the XNA/LZ4
  codecs this plugin deliberately does not depend on.
- Unreal `.uasset`/`.umap` facts come from the summary header; the name and export tables are counted, not
  enumerated, so asset-level names beyond the package name are not published.
- Unity package asset paths are samples taken from `pathname` entries, not an inventory of every asset.
- RPG Maker Marshal results depend on the walker understanding every opcode it meets; on an unknown opcode it
  stops and publishes `ScanIncomplete`.
