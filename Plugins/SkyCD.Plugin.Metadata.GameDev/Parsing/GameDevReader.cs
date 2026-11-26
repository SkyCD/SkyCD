using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>
/// Dispatches a claimed extension to its engine reader. Text formats read a bounded prefix, header formats read
/// only what the header needs, and archive formats stream from the central directory. Damaged or foreign content
/// yields <c>null</c> or a <c>Valid=false</c> record instead of an exception, so a scan never fails on one file.
/// </summary>
internal static class GameDevReader
{
    internal static async Task<GameDevFacts?> ReadAsync(MediaItem item, IMediaSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        var extension = item.Extension.ToLowerInvariant();
        try
        {
            return await ReadCoreAsync(extension, source, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static async Task<GameDevFacts?> ReadCoreAsync(string extension, IMediaSource source,
        CancellationToken cancellationToken)
    {
        switch (extension)
        {
            case ".aseprite":
            case ".ase":
                {
                    var (data, truncated) = await ReadWholeAsync(source, cancellationToken).ConfigureAwait(false);
                    var facts = AsepriteReader.Read(data);
                    if (facts is not null)
                    {
                        facts.ScanIncomplete |= truncated;
                    }

                    return facts;
                }

            case ".unitypackage":
                return UnityPackageReader.Read(source, cancellationToken);

            case ".c3p":
                return Construct3Reader.Read(source, cancellationToken);

            case ".love":
                return LoveReader.Read(source, cancellationToken);

            case ".yyz":
                {
                    var facts = GameMakerReader.ReadArchive(source, cancellationToken, out var projectBytes);
                    if (facts is not null && projectBytes is not null)
                    {
                        var project = GameMakerReader.ReadProject(GameDevSource.DecodeText(projectBytes), false);
                        if (project is not null)
                        {
                            facts.ProjectName = project.ProjectName;
                            facts.EngineVersion = project.EngineVersion;
                            facts.ResourceCount = project.ResourceCount;
                            facts.SceneCount = project.SceneCount;
                            facts.ImportCount = project.ImportCount;
                            facts.BlockCount = project.BlockCount;
                            facts.ChunkCount = project.ChunkCount;
                            foreach (var name in project.NameSamples)
                            {
                                facts.AddNameSample(name);
                            }
                        }
                    }

                    return facts;
                }

            case ".uasset":
            case ".umap":
                {
                    var (data, truncated) = await ReadWholeAsync(source, cancellationToken).ConfigureAwait(false);
                    return UnrealReader.ReadPackage(data, truncated);
                }

            case ".xnb":
                {
                    var (data, truncated) = await ReadWholeAsync(source, cancellationToken).ConfigureAwait(false);
                    var facts = MonoGameReader.Read(data);
                    if (facts is not null)
                    {
                        facts.ScanIncomplete |= truncated;
                    }

                    return facts;
                }

            case ".wiscene":
                {
                    var (data, truncated) = await ReadWholeAsync(source, cancellationToken).ConfigureAwait(false);
                    var facts = WickedReader.Read(data);
                    if (facts is not null)
                    {
                        facts.ScanIncomplete |= truncated;
                    }

                    return facts;
                }

            case ".res":
            case ".pck":
            case ".scn":
                {
                    var (data, truncated) = await ReadPrefixAsync(source, 64 * 1024, cancellationToken).ConfigureAwait(false);
                    return GodotReader.ReadBinary(extension, data, truncated);
                }

            case ".rbxl":
            case ".rbxm":
                {
                    var (data, truncated) = await ReadPrefixAsync(source, 64, cancellationToken).ConfigureAwait(false);
                    return RobloxReader.ReadBinary(data, truncated);
                }

            case ".rpa":
                {
                    var (data, _) = await ReadPrefixAsync(source, 192, cancellationToken).ConfigureAwait(false);
                    return RenpyReader.ReadArchive(data);
                }

            case ".rpyc":
                {
                    var (data, truncated) = await ReadPrefixAsync(source, 64 * 1024, cancellationToken).ConfigureAwait(false);
                    return RenpyReader.ReadCompiled(data, truncated);
                }

            case ".rxdata":
            case ".rvdata":
            case ".rvdata2":
                {
                    var (data, truncated) = await ReadPrefixAsync(source, GameDevLimits.MaxTextBytes,
                        cancellationToken).ConfigureAwait(false);
                    return RpgMakerReader.ReadMarshal(data, truncated);
                }

            case ".rpgproject":
            case ".rmmzproject":
                {
                    var (data, _) = await ReadPrefixAsync(source, 512, cancellationToken).ConfigureAwait(false);
                    return RpgMakerReader.ReadProjectText(GameDevSource.DecodeText(data));
                }

            case ".prefab":
            case ".scene":
            case ".material":
            case ".mat":
            case ".anim":
            case ".meta":
            case ".mtl":
            case ".tsx":
            case ".go":
            case ".project":
                return await ReadSharedAsync(extension, source, cancellationToken).ConfigureAwait(false);

            default:
                return await ReadTextFamilyAsync(extension, source, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Dispatches a shared extension by its content, using the same classifier as the format probe, so a
    /// foreign file never reaches an engine reader and two engines can claim one extension.
    /// </summary>
    private static async Task<GameDevFacts?> ReadSharedAsync(string extension, IMediaSource source,
        CancellationToken cancellationToken)
    {
        var (data, truncated) = await ReadPrefixAsync(source, GameDevLimits.MaxTextBytes, cancellationToken)
            .ConfigureAwait(false);
        var text = GameDevSource.DecodeText(data);

        return GameDevProbe.Classify(extension, data, text) switch
        {
            GameDevFormatKind.UnityYaml => UnityReader.Read(extension, text, truncated),
            GameDevFormatKind.UnityMeta => UnityReader.ReadMeta(text, truncated),
            GameDevFormatKind.CocosJson => CocosReader.ReadCreatorAsset(extension, text, truncated),
            GameDevFormatKind.CocosMeta => CocosReader.ReadMeta(text, truncated),
            GameDevFormatKind.O3dePrefab => O3deReader.ReadPrefab(text, truncated),
            GameDevFormatKind.O3deMaterial => O3deReader.ReadMaterial(text, truncated),
            GameDevFormatKind.FlaxAsset => FlaxReader.ReadAsset(extension, text, truncated),
            GameDevFormatKind.SandboxAsset => SandboxReader.ReadAsset(extension, text, truncated),
            GameDevFormatKind.DefoldResource => DefoldReader.Read(extension, text, truncated),
            GameDevFormatKind.DefoldProject => DefoldReader.ReadProject(text, truncated),
            GameDevFormatKind.CryMaterial => CryEngineReader.ReadMaterial(text, truncated),
            GameDevFormatKind.TiledTileset => TiledReader.ReadTileset(text, truncated),
            _ => null
        };
    }

    private static async Task<GameDevFacts?> ReadTextFamilyAsync(string extension, IMediaSource source,
        CancellationToken cancellationToken)
    {
        var (data, truncated) = await ReadPrefixAsync(source, GameDevLimits.MaxTextBytes, cancellationToken)
            .ConfigureAwait(false);
        var text = GameDevSource.DecodeText(data);

        switch (extension)
        {
            case ".tscn":
            case ".escn":
            case ".tres":
            case ".import":
            case ".gdextension":
            case ".godot":
                if (extension == ".godot"
                    && !text.Contains("config_version", StringComparison.OrdinalIgnoreCase)
                    && !text.Contains("[application]", StringComparison.Ordinal))
                {
                    return null;
                }

                return GodotReader.ReadText(extension, text, truncated);

            case ".unity":
            case ".asset":
            case ".controller":
            case ".overrideController":
            case ".physicMaterial":
                return UnityReader.Read(extension, text, truncated);

            case ".tmx":
            case ".world":
                return TiledReader.ReadXml(extension, text, truncated);

            case ".tmj":
            case ".tsj":
                return TiledReader.ReadJson(extension, text, truncated);

            case ".ldtk":
                return LdtkReader.Read(text, level: false, truncated);

            case ".ldtkl":
                return LdtkReader.Read(text, level: true, truncated);

            case ".uproject":
                return UnrealReader.ReadProject(text, plugin: false);

            case ".uplugin":
                return UnrealReader.ReadProject(text, plugin: true);

            case ".verse":
                return UnrealReader.ReadVerse(text, truncated);

            case ".rbxlx":
            case ".rbxmx":
                return RobloxReader.ReadXml(text, truncated);

            case ".yyp":
                return GameMakerReader.ReadProject(text, truncated);

            case ".yy":
                return GameMakerReader.ReadResource(text, truncated);

            case ".fire":
                return CocosReader.ReadCreator(text, truncated);

            case ".csd":
                return CocosReader.ReadStudio(text, truncated);

            case ".atlas":
                return LooksLikeLibGdxAtlas(text)
                    ? AtlasReader.Read(text, truncated)
                    : DefoldReader.Read(extension, text, truncated);

            case ".collection":
            case ".gui":
            case ".tilemap":
            case ".sprite":
                return DefoldReader.Read(extension, text, truncated);

            case ".cryproject":
            case ".cryproj":
                return CryEngineReader.ReadProject(text, truncated);

            case ".chrparams":
                return CryEngineReader.ReadCharacterParams(text, truncated);

            case ".slice":
                return O3deReader.ReadSlice(text, truncated);

            case ".azasset":
                return O3deReader.ReadAsset(text, truncated);

            case ".flaxproj":
                return FlaxReader.Read(text, truncated);

            case ".sdscene":
            case ".sdprefab":
            case ".sdmat":
            case ".sdtex":
                return StrideReader.Read(extension, text, truncated);

            case ".rpy":
            case ".rpym":
                return RenpyReader.ReadScript(text, truncated);

            case ".sbproj":
                return SandboxReader.Read(text, truncated);

            case ".mgcb":
                return MonoGameContentReader.Read(text, truncated);

            default:
                return null;
        }
    }

    /// <summary>Distinguishes a libGDX page-and-key block from a Defold protobuf-text atlas on the shared extension.</summary>
    private static bool LooksLikeLibGdxAtlas(string text) =>
        text.Contains(".png", StringComparison.OrdinalIgnoreCase)
        && (text.Contains("format:", StringComparison.Ordinal) || text.Contains("size:", StringComparison.Ordinal));

    private static Task<(byte[] Data, bool Truncated)> ReadPrefixAsync(IMediaSource source, int maxBytes,
        CancellationToken cancellationToken) =>
        GameDevSource.ReadPrefixAsync(source, maxBytes, cancellationToken);

    private static Task<(byte[] Data, bool Truncated)> ReadWholeAsync(IMediaSource source,
        CancellationToken cancellationToken) =>
        GameDevSource.ReadWholeAsync(source, GameDevLimits.MaxWholeSourceBytes, cancellationToken);
}
