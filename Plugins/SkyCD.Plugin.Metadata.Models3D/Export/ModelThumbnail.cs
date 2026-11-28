using System.Numerics;
using SkyCD.Plugin.Metadata.Models3D.Models;
using SkyCD.SDK.Capabilities.Media;
using SkiaSharp;

namespace SkyCD.Plugin.Metadata.Models3D.Export;

internal static class ModelThumbnail
{
    private const int Size = 512;
    private const int Padding = 20;

    public static MediaPreviewArtifact? Render(IReadOnlyList<ModelTriangle> triangles, CancellationToken cancellation)
    {
        if (triangles.Count == 0)
            return null;
        try
        {
            return Draw(triangles, cancellation);
        }
        catch (Exception error) when (error is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static MediaPreviewArtifact? Draw(IReadOnlyList<ModelTriangle> triangles, CancellationToken cancellation)
    {
        var rotation = Matrix4x4.CreateRotationY(0.65f) * Matrix4x4.CreateRotationX(-0.45f);
        var projected = new (Vector3 A, Vector3 B, Vector3 C, SKColor Color, float Depth)[triangles.Count];
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        var magnitude = triangles.Max(triangle => Math.Max(Magnitude(triangle.A),
            Math.Max(Magnitude(triangle.B), Magnitude(triangle.C))));
        if (magnitude == 0)
            return null;

        Vector3 Project(Vector3 position)
        {
            var point = Vector3.Transform(position / magnitude, rotation);
            var planar = new Vector2(point.X, point.Y);
            min = Vector2.Min(min, planar);
            max = Vector2.Max(max, planar);
            return point;
        }

        for (var index = 0; index < triangles.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var triangle = triangles[index];
            var a = Project(triangle.A);
            var b = Project(triangle.B);
            var c = Project(triangle.C);
            var normal = Vector3.Cross(b - a, c - a);
            var length = normal.Length();
            var light = length > 0 ? 0.35f + 0.65f * MathF.Abs(normal.Z / length) : 0.35f;
            var color = triangle.Color ?? new Vector4(0.44f, 0.70f, 0.90f, 1);
            projected[index] = (a, b, c, new SKColor(Channel(color.X * light), Channel(color.Y * light),
                Channel(color.Z * light), Channel(color.W)), a.Z + b.Z + c.Z);
        }

        var span = max - min;
        var largest = Math.Max(span.X, span.Y);
        if (largest <= 0)
            return null;
        var scale = (Size - Padding * 2) / largest;
        var width = Math.Min(Size, (int)Math.Ceiling(span.X * scale) + Padding * 2);
        var height = Math.Min(Size, (int)Math.Ceiling(span.Y * scale) + Padding * 2);
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint { IsAntialias = false };
        using var path = new SKPath();
        canvas.Clear(SKColors.Transparent);
        foreach (var triangle in projected.OrderBy(triangle => triangle.Depth))
        {
            cancellation.ThrowIfCancellationRequested();
            path.Rewind();
            path.MoveTo(Point(triangle.A));
            path.LineTo(Point(triangle.B));
            path.LineTo(Point(triangle.C));
            path.Close();
            paint.Color = triangle.Color;
            canvas.DrawPath(path, paint);
        }
        using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return png is { Size: <= 4 * 1024 * 1024 } ? new("image/png", png.ToArray()) : null;

        SKPoint Point(Vector3 point) => new(Padding + (point.X - min.X) * scale,
            Padding + (max.Y - point.Y) * scale);
    }

    private static float Magnitude(Vector3 point) => Math.Max(MathF.Abs(point.X),
        Math.Max(MathF.Abs(point.Y), MathF.Abs(point.Z)));

    private static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255 + 0.5f), 0, 255);
}
