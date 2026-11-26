using System;
using System.Collections.Generic;
using AsepriteDotNet.Aseprite;
using AsepriteDotNet.Aseprite.Types;
using AsepriteDotNet.Common;
using AsepriteDotNet.IO;
using SkyCD.Plugin.Metadata.GameDev.Models;

namespace SkyCD.Plugin.Metadata.GameDev.Parsing;

/// <summary>Reads Aseprite documents through AsepriteDotNet: canvas, frames, layers, tags, slices and tilesets.</summary>
internal static class AsepriteReader
{
    internal static GameDevFacts? Read(byte[] data)
    {
        AsepriteFile file;
        try
        {
            using var stream = new System.IO.MemoryStream(data, writable: false);
            file = AsepriteFileLoader.FromStream("aseprite.ase", stream, leaveOpen: false, preMultiplyAlpha: false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }

        var facts = new GameDevFacts
        {
            Family = "Aseprite",
            Format = "Aseprite",
            ImageWidth = file.CanvasWidth,
            ImageHeight = file.CanvasHeight,
            FrameCount = file.FrameCount,
            NodeCount = file.Layers.Length,
            TagCount = file.Tags.Length,
            ColorMode = ColorMode(file.ColorDepth),
            ColorDepth = ColorDepth(file.ColorDepth),
            HasThumbnail = file.FrameCount > 0
        };

        double duration = 0;
        foreach (var frame in file.Frames)
        {
            duration += frame.Duration.TotalMilliseconds;
        }

        if (duration > 0)
        {
            facts.TotalDurationMilliseconds = (long)Math.Round(duration);
        }

        var loops = false;
        foreach (var tag in file.Tags)
        {
            facts.AddAnimationName(tag.Name);
            if (tag.Repeat == 0)
            {
                loops = true;
            }
        }

        if (file.Tags.Length > 0)
        {
            facts.Loop = loops;
        }

        foreach (var tileset in file.Tilesets)
        {
            facts.AddTilesetName(tileset.Name);
        }

        foreach (var layer in file.Layers)
        {
            if (layer is AsepriteTilemapLayer)
            {
                facts.TileLayerCount = (facts.TileLayerCount ?? 0) + 1;
            }
        }

        facts.ResourceCount = file.Slices.Length;
        facts.AnimationNames.Sort(StringComparer.Ordinal);
        foreach (var name in facts.AnimationNames)
        {
            facts.AddPreviewRow(name);
        }

        return facts;
    }

    /// <summary>Flattens frame zero into an encoded thumbnail, or returns empty when decoding fails.</summary>
    internal static byte[]? TryRenderFrame(byte[] data, out string contentType)
    {
        contentType = "image/png";
        try
        {
            using var stream = new System.IO.MemoryStream(data, writable: false);
            var file = AsepriteFileLoader.FromStream("aseprite.ase", stream, leaveOpen: false, preMultiplyAlpha: false);
            if (file.FrameCount == 0)
            {
                return null;
            }

            var frame = file.Frames[0];
            Rgba32[] pixels = frame.FlattenFrame(onlyVisibleLayers: true, includeBackgroundLayer: true,
                includeTilemapCels: true);
            var width = frame.Size.Width;
            var height = frame.Size.Height;
            if (width <= 0 || height <= 0 || pixels.Length < width * height)
            {
                return null;
            }

            return PixelPreview.EncodeRgba(pixels, width, height, GameDevLimits.MaxThumbnailDimension,
                out contentType);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            contentType = "image/png";
            return null;
        }
    }

    private static string ColorMode(AsepriteColorDepth depth) => depth switch
    {
        AsepriteColorDepth.RGBA => "RGBA",
        AsepriteColorDepth.Grayscale => "Grayscale",
        AsepriteColorDepth.Indexed => "Indexed",
        _ => depth.ToString()
    };

    private static long ColorDepth(AsepriteColorDepth depth) => depth switch
    {
        AsepriteColorDepth.RGBA => 32,
        AsepriteColorDepth.Grayscale => 16,
        AsepriteColorDepth.Indexed => 8,
        _ => 0
    };
}
