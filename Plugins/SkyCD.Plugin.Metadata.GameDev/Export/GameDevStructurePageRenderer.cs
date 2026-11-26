using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Metadata.GameDev.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.GameDev.Export;

/// <summary>
/// Draws a bounded structure page for a project or scene: the family and format, the sizes, versions and
/// counts the reader found, and then the collected node, class or layout names.
/// </summary>
/// <remarks>
/// The page is a summary of what the metadata extraction already produced, so it never re-reads the source
/// and never shows more than <see cref="GameDevLimits.MaxPreviewRows"/> rows. A missing rasterizer returns
/// <c>null</c>; metadata stays intact without the image.
/// </remarks>
internal sealed class GameDevStructurePageRenderer
{
    internal Task<byte[]?> RenderAsync(GameDevFacts facts, int maxDimension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        try
        {
            return Task.FromResult(RenderCore(facts, maxDimension, cancellationToken));
        }
        catch (Exception exception) when (exception is DllNotFoundException
            or TypeInitializationException
            or EntryPointNotFoundException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException)
        {
            return Task.FromResult<byte[]?>(null);
        }
    }

    private static byte[]? RenderCore(GameDevFacts facts, int maxDimension, CancellationToken cancellationToken)
    {
        using var page = SkiaPageRenderer.TryCreate(maxDimension);
        if (page is null)
        {
            return null;
        }

        var heading = page.GetFont(SKFontStyle.Bold);
        var normal = page.GetFont(SKFontStyle.Normal);
        DrawLine(page, heading, facts.Family + " \u2014 " + facts.Format, cancellationToken);

        var summary = BuildSummary(facts);
        foreach (var row in summary)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawLine(page, normal, row, cancellationToken);
            if (page.IsFinished)
            {
                return page.EncodePng();
            }
        }

        if (facts.PreviewRows.Count > 0)
        {
            DrawLine(page, heading, "Names", cancellationToken);
            foreach (var row in facts.PreviewRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DrawLine(page, normal, row, cancellationToken);
                if (page.IsFinished)
                {
                    break;
                }
            }
        }

        return page.EncodePng();
    }

    private static void DrawLine(SkiaPageRenderer page, SKFont font, string text, CancellationToken cancellationToken)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        foreach (var token in SkiaPageText.TokenizePreservingNewlines(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token == "\n")
            {
                page.AdvanceLine();
                if (page.IsFinished)
                {
                    return;
                }

                continue;
            }

            if (!page.TryDrawToken(font, token))
            {
                return;
            }
        }

        page.AdvanceLine();
    }

    /// <summary>Builds the label lines shown above the name samples, omitting every absent fact.</summary>
    internal static List<string> BuildSummary(GameDevFacts facts)
    {
        var rows = new List<string>();
        AddText(rows, "Engine", facts.EngineVersion ?? facts.Application);
        AddText(rows, "Version", facts.FormatVersion);
        AddText(rows, "Project", facts.ProjectName);
        AddText(rows, "Item", facts.ItemName);
        AddSize(rows, facts);
        AddCount(rows, "Scenes", facts.SceneCount);
        AddCount(rows, "Levels", facts.LevelCount);
        AddCount(rows, "Nodes", facts.NodeCount);
        AddCount(rows, "Entities", facts.EntityCount);
        AddCount(rows, "Instances", facts.InstanceCount);
        AddCount(rows, "Objects", facts.ObjectCount);
        AddCount(rows, "Layers", facts.LayerCount);
        AddCount(rows, "Tile layers", facts.TileLayerCount);
        AddCount(rows, "Object groups", facts.ObjectGroupCount);
        AddCount(rows, "Frames", facts.FrameCount);
        AddCount(rows, "Tags", facts.TagCount);
        AddCount(rows, "Assets", facts.AssetCount);
        AddCount(rows, "Resources", facts.ResourceCount);
        AddCount(rows, "Imports", facts.ImportCount);
        AddCount(rows, "Exports", facts.ExportCount);
        AddCount(rows, "Files", facts.FileCount);
        AddCount(rows, "Scripts", facts.ScriptCount);
        AddCount(rows, "Classes", facts.ClassCount);
        AddDuration(rows, facts.TotalDurationMilliseconds);
        AddText(rows, "Color", facts.ColorMode);
        return rows;
    }

    private static void AddSize(List<string> rows, GameDevFacts facts)
    {
        if (facts.ImageWidth is > 0 && facts.ImageHeight is > 0)
        {
            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"Canvas: {facts.ImageWidth} \u00d7 {facts.ImageHeight} px"));
            return;
        }

        if (facts.MapWidth is > 0 && facts.MapHeight is > 0)
        {
            var builder = new StringBuilder();
            builder.Append(CultureInfo.InvariantCulture, $"Map: {facts.MapWidth} \u00d7 {facts.MapHeight}");
            if (facts.TileWidth is > 0 && facts.TileHeight is > 0)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $", tiles {facts.TileWidth} \u00d7 {facts.TileHeight} px");
            }

            rows.Add(builder.ToString());
        }
    }

    private static void AddDuration(List<string> rows, long? milliseconds)
    {
        if (milliseconds is > 0)
        {
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"Duration: {milliseconds.Value} ms"));
        }
    }

    private static void AddCount(List<string> rows, string label, long? count)
    {
        if (count.HasValue)
        {
            rows.Add(string.Create(CultureInfo.InvariantCulture, $"{label}: {count.Value}"));
        }
    }

    private static void AddText(List<string> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(label + ": " + value);
        }
    }
}
