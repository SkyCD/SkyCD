using System.Globalization;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Sequencer.Models;
using SkyCD.Plugin.Preview.Export;

namespace SkyCD.Plugin.Metadata.Sequencer.Export;

internal static class SequencerRenderer
{
    private static readonly SKColor[] Palette =
    [
        SKColor.Parse("#20767B"), SKColor.Parse("#225FAD"), SKColor.Parse("#8F446D"), SKColor.Parse("#A45620"),
        SKColor.Parse("#5C54A2"), SKColor.Parse("#678222"), SKColor.Parse("#A74255"), SKColor.Parse("#487587")
    ];

    internal static byte[]? Render(SequencerFacts facts, bool midi, CancellationToken token)
    {
        if (midi && facts.Notes.Count == 0)
            return null;

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
        DrawText(midi ? $"SMF {Value("MidiFormat")}   {Value("Tempo")} BPM" :
            string.Join("  ", new[] { application, Value("ApplicationVersion") }.Where(value => value is not null)), 20, 53, 13);

        if (midi)
        {
            const int left = 20, top = 66, width = 472, height = 298;
            const float row = height / 88f;
            float X(double seconds) => left + (float)(Math.Clamp(seconds, 0, 120) / 120) * width;
            for (var pitch = 21; pitch <= 108; pitch++)
            {
                var black = pitch % 12 is 1 or 3 or 6 or 8 or 10;
                paint.Color = black ? SKColor.Parse("#E0E6EF") : SKColor.Parse("#EDF1F7");
                canvas.DrawRect(left, top + (108 - pitch) * row, width, row, paint);
            }
            paint.Color = SKColor.Parse("#BDC8D9");
            foreach (var beat in facts.Beats)
                canvas.DrawLine(X(beat), top, X(beat), top + height, paint);

            foreach (var note in facts.Notes)
            {
                token.ThrowIfCancellationRequested();
                paint.Color = Palette[note.Channel % Palette.Length];
                var x = X(note.Start);
                canvas.DrawRect(x, top + (108 - note.Pitch) * row, Math.Max(1, X(note.End) - x), row - 0.3f, paint);
            }
        }
        else
        {
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
