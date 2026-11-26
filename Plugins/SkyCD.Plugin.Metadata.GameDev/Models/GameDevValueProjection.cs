using System.Collections.Generic;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.GameDev.Models;

/// <summary>
/// Projects collected facts onto the metadata schema. Absent values are omitted rather than published as
/// zero or an empty string, so a reader that could not see a section never claims a count for it.
/// </summary>
internal static class GameDevValueProjection
{
    internal static List<MediaMetadataValue> Build(GameDevFacts facts)
    {
        var values = new List<MediaMetadataValue>();
        if (facts.Family.Length == 0 || facts.Format.Length == 0)
        {
            return values;
        }

        AddText(values, GameDevSchema.Family, facts.Family);
        AddText(values, GameDevSchema.Format, facts.Format);
        AddText(values, GameDevSchema.FormatVersion, facts.FormatVersion);
        AddText(values, GameDevSchema.EngineVersion, facts.EngineVersion);
        AddText(values, GameDevSchema.Application, facts.Application);
        AddText(values, GameDevSchema.ProjectName, facts.ProjectName);
        AddText(values, GameDevSchema.ItemName, facts.ItemName);
        AddBoolean(values, GameDevSchema.Compiled, facts.Compiled);
        AddBoolean(values, GameDevSchema.Cooked, facts.Cooked);

        // A valid document needs no flag; only a recognized-but-invalid one carries the explicit state.
        if (facts.Valid == false)
        {
            values.Add(new MediaMetadataValue(GameDevSchema.Valid, MediaMetadataType.Boolean, Boolean: false));
        }

        if (facts.ScanIncomplete)
        {
            values.Add(new MediaMetadataValue(GameDevSchema.ScanIncomplete, MediaMetadataType.Boolean,
                Boolean: true));
        }

        AddInteger(values, GameDevSchema.NodeCount, facts.NodeCount);
        AddInteger(values, GameDevSchema.EntityCount, facts.EntityCount);
        AddInteger(values, GameDevSchema.InstanceCount, facts.InstanceCount);
        AddInteger(values, GameDevSchema.ObjectCount, facts.ObjectCount);
        AddInteger(values, GameDevSchema.SceneCount, facts.SceneCount);
        AddInteger(values, GameDevSchema.LevelCount, facts.LevelCount);
        AddInteger(values, GameDevSchema.LayerCount, facts.LayerCount);
        AddInteger(values, GameDevSchema.TileLayerCount, facts.TileLayerCount);
        AddInteger(values, GameDevSchema.ObjectGroupCount, facts.ObjectGroupCount);
        AddInteger(values, GameDevSchema.FrameCount, facts.FrameCount);
        AddInteger(values, GameDevSchema.TagCount, facts.TagCount);
        AddInteger(values, GameDevSchema.AssetCount, facts.AssetCount);
        AddInteger(values, GameDevSchema.ResourceCount, facts.ResourceCount);
        AddInteger(values, GameDevSchema.ImportCount, facts.ImportCount);
        AddInteger(values, GameDevSchema.ExportCount, facts.ExportCount);
        AddInteger(values, GameDevSchema.FileCount, facts.FileCount);
        AddInteger(values, GameDevSchema.ScriptCount, facts.ScriptCount);
        AddInteger(values, GameDevSchema.LabelCount, facts.LabelCount);
        AddInteger(values, GameDevSchema.CharacterCount, facts.CharacterCount);
        AddInteger(values, GameDevSchema.TrackCount, facts.TrackCount);
        AddInteger(values, GameDevSchema.ClipCount, facts.ClipCount);
        AddInteger(values, GameDevSchema.ChunkCount, facts.ChunkCount);
        AddInteger(values, GameDevSchema.BlockCount, facts.BlockCount);
        AddInteger(values, GameDevSchema.ClassCount, facts.ClassCount);
        AddInteger(values, GameDevSchema.ImageWidth, facts.ImageWidth);
        AddInteger(values, GameDevSchema.ImageHeight, facts.ImageHeight);
        AddInteger(values, GameDevSchema.MapWidth, facts.MapWidth);
        AddInteger(values, GameDevSchema.MapHeight, facts.MapHeight);
        AddInteger(values, GameDevSchema.TileWidth, facts.TileWidth);
        AddInteger(values, GameDevSchema.TileHeight, facts.TileHeight);
        AddText(values, GameDevSchema.ColorMode, facts.ColorMode);
        AddInteger(values, GameDevSchema.ColorDepth, facts.ColorDepth);
        AddInteger(values, GameDevSchema.TotalDuration, facts.TotalDurationMilliseconds);
        AddBoolean(values, GameDevSchema.Loop, facts.Loop);

        if (facts.HasThumbnail)
        {
            values.Add(new MediaMetadataValue(GameDevSchema.HasThumbnail, MediaMetadataType.Boolean,
                Boolean: true));
        }

        AddList(values, GameDevSchema.AnimationNames, facts.AnimationNames);
        AddList(values, GameDevSchema.NameSamples, facts.NameSamples);
        AddList(values, GameDevSchema.TilesetNames, facts.TilesetNames);
        return values;
    }

    private static void AddText(List<MediaMetadataValue> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: value));
        }
    }

    private static void AddInteger(List<MediaMetadataValue> values, string key, long? value)
    {
        if (value.HasValue)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Integer, Integer: value.Value));
        }
    }

    private static void AddBoolean(List<MediaMetadataValue> values, string key, bool? value)
    {
        if (value.HasValue)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Boolean, Boolean: value.Value));
        }
    }

    private static void AddList(List<MediaMetadataValue> values, string key, List<string> entries)
    {
        if (entries.Count > 0)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text,
                Text: string.Join("; ", entries)));
        }
    }
}
