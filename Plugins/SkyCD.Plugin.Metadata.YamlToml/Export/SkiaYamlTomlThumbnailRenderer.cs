using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkiaSharp;
using SkyCD.Plugin.Metadata.YamlToml.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.YamlToml.Export;

/// <summary>
/// Draws keys in bold, values in the regular face and comments in italic on one shared page.
/// A missing rasterizer returns null so metadata is still published.
/// </summary>
public sealed class SkiaYamlTomlThumbnailRenderer : IYamlTomlThumbnailRenderer
{
    public bool IsAvailable => true;

    public Task<byte[]?> RenderAsync(
        IReadOnlyList<YamlTomlPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDimension);

        try
        {
            return Task.FromResult(RenderCore(lines, maxDimension, cancellationToken));
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

    private static byte[]? RenderCore(
        IReadOnlyList<YamlTomlPreviewLine> lines,
        int maxDimension,
        CancellationToken cancellationToken)
    {
        using var page = SkiaPageRenderer.TryCreate(maxDimension, beginAtMargin: true);
        if (page is null)
        {
            return null;
        }

        var regular = page.GetFont(SKFontStyle.Normal);
        var bold = page.GetFont(SKFontStyle.Bold);
        var italic = page.GetFont(SKFontStyle.Italic);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            page.AdvanceLine(page.LineHeight);
            if (page.IsFinished)
            {
                break;
            }

            foreach (var span in line.Spans)
            {
                var font = span.Kind switch
                {
                    YamlTomlSpanKind.Key => bold,
                    YamlTomlSpanKind.Comment => italic,
                    _ => regular
                };

                foreach (var token in SkiaPageText.TokenizePreservingNewlines(span.Text))
                {
                    if (token.Contains('\n'))
                    {
                        page.AdvanceLine(page.LineHeight);
                        if (page.IsFinished)
                        {
                            return page.EncodePng();
                        }

                        continue;
                    }

                    if (!page.TryDrawToken(font, token, wrapLineHeight: page.LineHeight))
                    {
                        return page.EncodePng();
                    }
                }
            }
        }

        return page.EncodePng();
    }
}
