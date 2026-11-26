using System.Globalization;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Sequencer.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Sequencer.Export;

internal static class SequencerRenderer
{
    internal static byte[]? Render(SequencerFacts facts, CancellationToken token)
    {
        using var surface = SKSurface.Create(new SKImageInfo(512, 384));
        if (surface is null)
            return null;
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse("#F5F7FB"));
        using var font = new SKFont(SKTypeface.Default, 14);
        using var paint = new SKPaint { IsAntialias = true };
        var values = facts.Values.ToDictionary(value => value.Key, StringComparer.Ordinal);
        string? Value(string key) => values.TryGetValue(key, out var value)
            ? value.Text ?? value.Integer?.ToString(CultureInfo.InvariantCulture)
              ?? value.Number?.ToString("0.##", CultureInfo.InvariantCulture)
              ?? value.Boolean?.ToString()
            : null;
        var application = Value("Application") ?? Value("Tracker") ?? Value("Format");
        var title = Value("Title") ?? application ?? "Sequencer";
        DrawText(title, 20, 31, 22);
        DrawText(string.Join("  ", new[] { application, Value("ApplicationVersion") }.Where(value => value is not null)), 20, 53, 13);

        var y = 80f;
        foreach (var value in facts.Values.Where(value => value.Key is not ("Kind" or "Format" or "Title" or "Application"
                     or "ApplicationVersion" or "Tracker")))
        {
            token.ThrowIfCancellationRequested();
            DrawText(value.Key, 20 + (y >= 240 ? 236 : 0), y >= 240 ? y - 160 : y, 13);
            DrawText(Value(value.Key) ?? "", 134 + (y >= 240 ? 236 : 0), y >= 240 ? y - 160 : y, 13, 112);
            y += 20;
            if (y >= 400)
                break;
        }
        var index = 0;
        foreach (var name in facts.Names.Take(30))
        {
            token.ThrowIfCancellationRequested();
            DrawText(name, 20 + index % 3 * 158, 248 + index / 3 * 13, 11, 148);
            index++;
        }

        using var image = surface.Snapshot();
        return PreviewArtifactEncoder.EncodePng(image);

        void DrawText(string text, float x, float baseline, float size, float width = 472)
        {
            font.Size = size;
            paint.Color = SKColor.Parse("#202E44");
            var length = font.BreakText(text, width);
            canvas.DrawText(text[..(int)Math.Min(length, text.Length)], x, baseline, SKTextAlign.Left, font, paint);
        }
    }
}
