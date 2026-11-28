using System.Numerics;
using System.Text.Json;
using SharpGLTF.Schema2;
using SkyCD.Plugin.Metadata.Models3D.Exceptions;
using SkyCD.Plugin.Metadata.Models3D.Models;

namespace SkyCD.Plugin.Metadata.Models3D.Parsing;

internal static class GltfReader
{
    private const string Draco = "KHR_draco_mesh_compression";

    public static void Read(ReadOnlyMemory<byte> json, byte[] source, ModelScan scan, bool binary = false)
    {
        if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            json = json[3..];
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("asset", out var asset) || Text(asset, "version") != "2.0")
            throw new ModelReadException("InvalidAsset");

        scan.Text("Version", Text(asset, "version"));
        scan.Text("Generator", Text(asset, "generator"));
        var extensions = Array(root, "extensionsUsed").Select(element => element.GetString() ?? "").ToArray();
        scan.List("ExtensionsUsed", extensions);
        foreach (var (name, key) in new (string Name, string Key)[]
        {
            ("nodes", "NodeCount"), ("meshes", "MeshCount"), ("materials", "MaterialCount"),
            ("textures", "TextureCount"), ("images", "ImageCount"), ("animations", "AnimationCount"),
            ("skins", "SkinCount")
        })
            scan.Count(key, Array(root, name).Count());

        var buffers = Array(root, "buffers").ToArray();
        var embedded = buffers.Length > 0 && buffers.Select((buffer, index) =>
            Text(buffer, "uri") is { } uri ? IsDataUri(uri) : binary && index == 0).All(value => value);
        scan.Flag("HasEmbeddedBuffers", embedded);
        var externalImage = Array(root, "images").Any(image => Text(image, "uri") is { } uri && !IsDataUri(uri));

        var accessors = Array(root, "accessors").ToArray();
        var usesDraco = extensions.Contains(Draco);
        long primitives = 0, vertices = 0, triangles = 0;
        foreach (var mesh in Array(root, "meshes"))
        {
            foreach (var primitive in Array(mesh, "primitives"))
            {
                scan.Cancellation.ThrowIfCancellationRequested();
                primitives++;
                usesDraco |= primitive.TryGetProperty("extensions", out var primitiveExtensions) &&
                    primitiveExtensions.TryGetProperty(Draco, out _);
                if (primitive.TryGetProperty("attributes", out var attributes) &&
                    attributes.TryGetProperty("POSITION", out var position))
                {
                    var accessor = Accessor(position);
                    var count = Count(accessor);
                    vertices += count;
                    var corners = primitive.TryGetProperty("indices", out var indices) ? Count(Accessor(indices)) : count;
                    var mode = primitive.TryGetProperty("mode", out var modeValue) ? modeValue.GetInt32() : 4;
                    triangles += mode switch
                    {
                        4 => corners / 3,
                        5 or 6 => Math.Max(0, corners - 2),
                        _ => 0
                    };
                }
            }
        }

        scan.Count("PrimitiveCount", primitives);
        scan.Count("VertexCount", vertices);
        scan.Count("TriangleCount", triangles);

        if (scan.Truncated || !embedded || externalImage || usesDraco || vertices == 0)
            return;

        if (!CanDecode(root, accessors, scan))
        {
            scan.Incomplete = true;
            return;
        }

        // The resolver rejects every external lookup even if an extension supplies another URI.
        using var stream = binary ? new MemoryStream(source) : new MemoryStream(json.ToArray());
        var context = ReadContext.Create(_ => throw new ModelReadException("ExternalResource"));
        var model = context.ReadSchema2(stream);
        var nodes = model.DefaultScene is { } scene ? Node.Flatten(scene).Where(node => node.Mesh is not null).ToArray() : [];
        var remainingVertices = ModelScan.VertexLimit;
        if (nodes is { Length: > 0 })
        {
            foreach (var node in nodes)
                ReadMesh(node.Mesh!, node.WorldMatrix);
        }
        else
        {
            foreach (var mesh in model.LogicalMeshes)
                ReadMesh(mesh, Matrix4x4.Identity);
        }

        void ReadMesh(Mesh mesh, Matrix4x4 transform)
        {
            foreach (var primitive in mesh.Primitives)
            {
                scan.Cancellation.ThrowIfCancellationRequested();
                var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                if (positions is null)
                    continue;
                var count = Math.Min(positions.Count, remainingVertices);
                remainingVertices -= count;
                if (count < positions.Count)
                    scan.Incomplete = true;
                for (var index = 0; index < count; index++)
                {
                    if (index % 4096 == 0)
                        scan.Cancellation.ThrowIfCancellationRequested();
                    scan.Include(Vector3.Transform(positions[index], transform));
                }

                if (count == 0)
                    continue;
                var color = primitive.Material?.FindChannel("BaseColor")?.Color;
                foreach (var (a, b, c) in primitive.GetTriangleIndices())
                {
                    scan.Cancellation.ThrowIfCancellationRequested();
                    if (scan.Triangles.Count >= ModelScan.TriangleLimit)
                    {
                        scan.Incomplete = true;
                        break;
                    }
                    if (a < count && b < count && c < count)
                        scan.Triangle(Vector3.Transform(positions[a], transform),
                            Vector3.Transform(positions[b], transform), Vector3.Transform(positions[c], transform), color);
                }
            }
        }

        JsonElement Accessor(JsonElement index)
        {
            var value = index.GetInt32();
            return (uint)value < accessors.Length ? accessors[value] : throw new ModelReadException("InvalidAccessor");
        }
    }

    private static bool CanDecode(JsonElement root, JsonElement[] accessors, ModelScan scan)
    {
        // Bound decoder work even when a tiny sparse accessor declares a very large logical array.
        if (accessors.Sum(accessor => (long)Count(accessor)) > 4_000_000)
            return false;

        var nodes = Array(root, "nodes").ToArray();
        var parents = new int[nodes.Length];
        System.Array.Fill(parents, -1);
        for (var parent = 0; parent < nodes.Length; parent++)
        {
            scan.Cancellation.ThrowIfCancellationRequested();
            foreach (var child in Array(nodes[parent], "children"))
            {
                var index = child.GetInt32();
                if ((uint)index >= parents.Length || parents[index] >= 0)
                    throw new ModelReadException("InvalidNodeHierarchy");
                parents[index] = parent;
            }
        }

        // SharpGLTF walks parent chains recursively for world transforms.
        var depths = new int[nodes.Length];
        var chain = new HashSet<int>();
        for (var node = 0; node < nodes.Length; node++)
        {
            scan.Cancellation.ThrowIfCancellationRequested();
            if (depths[node] > 0)
                continue;
            chain.Clear();
            var parent = node;
            while (parent >= 0 && depths[parent] == 0)
            {
                if (!chain.Add(parent))
                    throw new ModelReadException("InvalidNodeHierarchy");
                if (chain.Count > 128)
                    return false;
                parent = parents[parent];
            }
            var depth = parent >= 0 ? depths[parent] : 0;
            if (depth + chain.Count > 128)
                return false;
            for (int index = node, remaining = chain.Count; remaining > 0; index = parents[index], remaining--)
                depths[index] = depth + remaining;
        }
        return true;
    }

    private static int Count(JsonElement accessor) =>
        accessor.TryGetProperty("count", out var count) && count.GetInt32() >= 0
            ? count.GetInt32() : throw new ModelReadException("InvalidAccessor");

    private static IEnumerable<JsonElement> Array(JsonElement element, string key) =>
        element.TryGetProperty(key, out var array) ? array.EnumerateArray() : [];

    private static string? Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) ? value.GetString() : null;

    private static bool IsDataUri(string uri) => uri.StartsWith("data:", StringComparison.Ordinal);
}
