using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>The document a shared extension actually carries, decided from the bytes.</summary>
internal enum GameDevFormatKind
{
    Unknown,
    UnityYaml,
    UnityMeta,
    CocosJson,
    CocosMeta,
    O3dePrefab,
    O3deMaterial,
    FlaxAsset,
    SandboxAsset,
    DefoldResource,
    DefoldProject,
    CryMaterial,
    TiledTileset,
    GodotBinary
}

/// <summary>
/// Classifies shared extensions from their content so several engines and the generic text provider can claim
/// the same extension. Markers are the ones named in the plugin documentation; a JSON document that does not
/// parse inside the probe budget is still classified by a bounded marker window, because a truncated prefix is
/// not proof that the file belongs to another format.
/// </summary>
internal static class GameDevProbe
{
    /// <summary>Bytes inspected for a root marker when the prefix is too short to parse as a whole document.</summary>
    private const int MarkerWindow = 4096;

    /// <summary>True when the provider should extract the item; only shared extensions are inspected.</summary>
    internal static async ValueTask<bool> AcceptsAsync(MediaItem item, IMediaSource source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);

        if (!GameDevSchema.IsSharedExtension(item.Extension))
        {
            return true;
        }

        var (data, _) = await GameDevSource
            .ReadPrefixAsync(source, GameDevLimits.MaxProbeBytes, cancellationToken).ConfigureAwait(false);
        return Classify(item.Extension, data, GameDevSource.DecodeText(data)) != GameDevFormatKind.Unknown;
    }

    internal static GameDevFormatKind Classify(string extension, ReadOnlySpan<byte> data, string text)
    {
        if (extension.Equals(".scn", StringComparison.OrdinalIgnoreCase))
        {
            return GameDevSource.StartsWithAscii(data, "RSRC") || GameDevSource.StartsWithAscii(data, "RSCC")
                ? GameDevFormatKind.GodotBinary
                : GameDevFormatKind.Unknown;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return GameDevFormatKind.Unknown;
        }

        return extension.ToLowerInvariant() switch
        {
            ".prefab" => ClassifyAsset(text, prefab: true),
            ".scene" => ClassifyAsset(text, prefab: false),
            ".material" => ClassifyMaterial(text),
            ".mat" => HasUnityHeader(text) ? GameDevFormatKind.UnityYaml : GameDevFormatKind.Unknown,
            ".anim" => ClassifyAnim(text),
            ".meta" => ClassifyMeta(text),
            ".mtl" => ClassifyMtl(text),
            ".tsx" => IsXmlRoot(text, "tileset")
                ? GameDevFormatKind.TiledTileset
                : GameDevFormatKind.Unknown,
            ".go" => IsDefoldResource(text) ? GameDevFormatKind.DefoldResource : GameDevFormatKind.Unknown,
            ".project" => IsDefoldProject(text) ? GameDevFormatKind.DefoldProject : GameDevFormatKind.Unknown,
            _ => GameDevFormatKind.Unknown
        };
    }

    private static GameDevFormatKind ClassifyAsset(string text, bool prefab)
    {
        if (HasUnityHeader(text))
        {
            return GameDevFormatKind.UnityYaml;
        }

        if (TryGetRoot(text, tolerant: true, out var document, out var root))
        {
            using (document)
            {
                if (HasProperty(root, "__type__"))
                {
                    return GameDevFormatKind.CocosJson;
                }

                if (prefab && HasProperty(root, "ContainerEntity"))
                {
                    return GameDevFormatKind.O3dePrefab;
                }

                if (HasProperty(root, "TypeName") && HasProperty(root, "EngineBuild"))
                {
                    return GameDevFormatKind.FlaxAsset;
                }

                if (HasProperty(root, "__class") && HasProperty(root, "__guid"))
                {
                    return GameDevFormatKind.SandboxAsset;
                }

                return GameDevFormatKind.Unknown;
            }
        }

        var head = Head(text);
        if (prefab && head.Contains("\"ContainerEntity\"", StringComparison.Ordinal))
        {
            return GameDevFormatKind.O3dePrefab;
        }

        if (head.Contains("\"TypeName\"", StringComparison.Ordinal)
            && head.Contains("\"EngineBuild\"", StringComparison.Ordinal))
        {
            return GameDevFormatKind.FlaxAsset;
        }

        if (head.Contains("\"__class\"", StringComparison.Ordinal)
            && head.Contains("\"__guid\"", StringComparison.Ordinal))
        {
            return GameDevFormatKind.SandboxAsset;
        }

        return head.Contains("\"__type__\"", StringComparison.Ordinal)
            ? GameDevFormatKind.CocosJson
            : GameDevFormatKind.Unknown;
    }

    private static GameDevFormatKind ClassifyMaterial(string text)
    {
        if (HasUnityHeader(text))
        {
            return GameDevFormatKind.UnityYaml;
        }

        if (TryGetRoot(text, tolerant: true, out var document, out var root))
        {
            using (document)
            {
                if (HasProperty(root, "__type__"))
                {
                    return GameDevFormatKind.CocosJson;
                }

                if (HasProperty(root, "materialType") && HasProperty(root, "propertyValues"))
                {
                    return GameDevFormatKind.O3deMaterial;
                }

                return HasProperty(root, "TypeName") && HasProperty(root, "EngineBuild")
                    ? GameDevFormatKind.FlaxAsset
                    : GameDevFormatKind.Unknown;
            }
        }

        if (IsDefoldMaterial(text))
        {
            return GameDevFormatKind.DefoldResource;
        }

        var head = Head(text);
        if (head.Contains("\"materialType\"", StringComparison.Ordinal)
            && head.Contains("\"propertyValues\"", StringComparison.Ordinal))
        {
            return GameDevFormatKind.O3deMaterial;
        }

        if (head.Contains("\"TypeName\"", StringComparison.Ordinal)
            && head.Contains("\"EngineBuild\"", StringComparison.Ordinal))
        {
            return GameDevFormatKind.FlaxAsset;
        }

        return head.Contains("\"__type__\"", StringComparison.Ordinal)
            ? GameDevFormatKind.CocosJson
            : GameDevFormatKind.Unknown;
    }

    private static GameDevFormatKind ClassifyAnim(string text)
    {
        if (HasUnityHeader(text))
        {
            return GameDevFormatKind.UnityYaml;
        }

        if (TryGetRoot(text, tolerant: true, out var document, out var root))
        {
            using (document)
            {
                return HasProperty(root, "__type__")
                    ? GameDevFormatKind.CocosJson
                    : GameDevFormatKind.Unknown;
            }
        }

        return Head(text).Contains("\"__type__\"", StringComparison.Ordinal)
            ? GameDevFormatKind.CocosJson
            : GameDevFormatKind.Unknown;
    }

    private static GameDevFormatKind ClassifyMeta(string text)
    {
        if (IsUnityMeta(text))
        {
            return GameDevFormatKind.UnityMeta;
        }

        if (TryGetRoot(text, tolerant: true, out var document, out var root))
        {
            using (document)
            {
                return HasProperty(root, "uuid") && HasProperty(root, "importer")
                    ? GameDevFormatKind.CocosMeta
                    : GameDevFormatKind.Unknown;
            }
        }

        var head = Head(text);
        return head.Contains("\"uuid\"", StringComparison.Ordinal)
            && head.Contains("\"importer\"", StringComparison.Ordinal)
                ? GameDevFormatKind.CocosMeta
                : GameDevFormatKind.Unknown;
    }

    private static GameDevFormatKind ClassifyMtl(string text)
    {
        if (IsXmlRoot(text, "Material"))
        {
            return GameDevFormatKind.CryMaterial;
        }

        if (TryGetRoot(text, tolerant: true, out var document, out var root))
        {
            using (document)
            {
                return HasProperty(root, "__type__")
                    ? GameDevFormatKind.CocosJson
                    : GameDevFormatKind.Unknown;
            }
        }

        return Head(text).Contains("\"__type__\"", StringComparison.Ordinal)
            ? GameDevFormatKind.CocosJson
            : GameDevFormatKind.Unknown;
    }

    private static bool TryGetRoot(string text, bool tolerant, out JsonDocument document, out JsonElement root)
    {
        if (GameDevDocuments.TryParseJson(text, out document, tolerant))
        {
            root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
        }

        root = default;
        return false;
    }

    private static bool HasProperty(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out _);

    private static bool HasUnityHeader(string text) =>
        text.Contains("%YAML 1.1", StringComparison.Ordinal)
        && text.Contains("tag:unity3d.com", StringComparison.Ordinal);

    private static bool IsUnityMeta(string text) =>
        text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n').StartsWith("fileFormatVersion", StringComparison.Ordinal);

    private static bool IsDefoldResource(string text) =>
        text.Contains("components {", StringComparison.Ordinal)
        || text.Contains("embedded_instances {", StringComparison.Ordinal)
        || text.Contains("embedded_components {", StringComparison.Ordinal);

    private static bool IsDefoldMaterial(string text) =>
        text.Contains("fragment_program:", StringComparison.Ordinal)
        || text.Contains("vertex_program:", StringComparison.Ordinal);

    private static bool IsDefoldProject(string text)
    {
        var project = false;
        var standard = false;
        foreach (var section in IniSections.Parse(text).SectionNames)
        {
            if (section.Equals("project", StringComparison.OrdinalIgnoreCase))
            {
                project = true;
            }
            else if (section is "display" or "bootstrap" or "input" or "physics" or "sound" or "graphics")
            {
                standard = true;
            }
        }

        return project && standard;
    }

    private static bool IsXmlRoot(string text, string localName)
    {
        try
        {
            using var reader = GameDevDocuments.CreateXml(text);
            return GameDevDocuments.ReadRootElement(reader)
                && string.Equals(reader.LocalName, localName, StringComparison.Ordinal);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string Head(string text) => text.Length <= MarkerWindow ? text : text[..MarkerWindow];
}
