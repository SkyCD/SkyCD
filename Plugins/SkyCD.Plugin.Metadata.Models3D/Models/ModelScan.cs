using System.Numerics;
using SkyCD.Plugin.Metadata.Models3D.Exceptions;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Models3D.Models;

internal sealed class ModelScan(string format, bool truncated, CancellationToken cancellation)
{
    public const int VertexLimit = 200_000;
    public const int TriangleLimit = 50_000;
    private readonly List<MediaMetadataValue> values = [new("Format", MediaMetadataType.Text, Text: format)];
    private Vector3? minimum;
    private Vector3? maximum;

    public CancellationToken Cancellation => cancellation;
    public bool Truncated { get; } = truncated;
    public bool Incomplete { get; set; } = truncated;
    public List<Vector3> Vertices { get; } = [];
    public List<ModelTriangle> Triangles { get; } = [];

    public void Text(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values.Add(new(key, MediaMetadataType.Text, Text: value[..Math.Min(value.Length, 2000)]));
    }

    public void List(string key, IEnumerable<string> entries) => Text(key, string.Join("; ", entries.Take(20)));
    public void Count(string key, long value) => values.Add(new(key, MediaMetadataType.Integer, Integer: value));
    public void Flag(string key, bool value) => values.Add(new(key, MediaMetadataType.Boolean, Boolean: value));

    public void Include(Vector3 vertex)
    {
        if (!float.IsFinite(vertex.X) || !float.IsFinite(vertex.Y) || !float.IsFinite(vertex.Z))
            throw new ModelReadException("InvalidVertex");

        minimum = minimum is { } min ? Vector3.Min(min, vertex) : vertex;
        maximum = maximum is { } max ? Vector3.Max(max, vertex) : vertex;
    }

    public void Vertex(Vector3 vertex)
    {
        Include(vertex);
        if (Vertices.Count < VertexLimit)
            Vertices.Add(vertex);
        else
            Incomplete = true;
    }

    public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector4? color = null)
    {
        if (Triangles.Count < TriangleLimit)
            Triangles.Add(new(a, b, c, color));
        else
            Incomplete = true;
    }

    public void Face(int first, int previous, int current, long vertexCount)
    {
        if (first < 0 || previous < 0 || current < 0 ||
            first >= vertexCount || previous >= vertexCount || current >= vertexCount)
            throw new ModelReadException("InvalidIndex");

        if (first < Vertices.Count && previous < Vertices.Count && current < Vertices.Count)
            Triangle(Vertices[first], Vertices[previous], Vertices[current]);
    }

    public IReadOnlyList<MediaMetadataValue> Finish()
    {
        if (minimum is { } min && maximum is { } max)
        {
            Text("BoundingBoxMin", FormattableString.Invariant($"{min.X:R} {min.Y:R} {min.Z:R}"));
            Text("BoundingBoxMax", FormattableString.Invariant($"{max.X:R} {max.Y:R} {max.Z:R}"));
        }
        if (Incomplete)
            Flag("ScanIncomplete", true);
        return values;
    }
}
