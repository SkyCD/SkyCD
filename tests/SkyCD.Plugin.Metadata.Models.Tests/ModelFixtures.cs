using System.Buffers.Binary;
using System.Text;

namespace SkyCD.Plugin.Metadata.Models.Tests;

internal static class ModelFixtures
{
    private static readonly float[] Positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];

    public const string Obj = "o model\ng group\nmtllib absent.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl test\nf -3 -2 -1\n";
    public const string Off = "OFF\n# quad\n4 1 0\n0 0 0\n1 0 0\n1 1 0\n0 1 0\n4 0 1 2 3\n";
    public const string Stl = """
        solid example
          facet normal 0 0 1
            outer loop
              vertex 0 0 0
              vertex 1 0 0
              vertex 0 1 0
            endloop
          endfacet
        endsolid example
        """;

    public static byte[] BinaryStl()
    {
        var data = new byte[134];
        Encoding.ASCII.GetBytes("solid binary").CopyTo(data, 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(80), 1);
        PositionBytes().CopyTo(data, 96);
        return data;
    }

    public static byte[] Ply(string encoding, bool pointCloud = false)
    {
        var face = pointCloud ? "" : "element face 1\nproperty list uchar int vertex_indices\n";
        var header = Encoding.ASCII.GetBytes(
            $"ply\nformat {encoding} 1.0\ncomment fixture\nelement vertex 3\nproperty float x\nproperty float y\nproperty float z\n{face}end_header\n");
        if (encoding == "ascii")
            return [.. header, .. Encoding.ASCII.GetBytes("0 0 0\n1 0 0\n0 1 0\n" + (pointCloud ? "" : "3 0 1 2\n"))];
        var body = pointCloud ? PositionBytes() : [.. PositionBytes(), 3, 0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0];
        if (encoding == "binary_big_endian")
        {
            for (var index = 0; index < 36; index += 4)
                body.AsSpan(index, 4).Reverse();
            for (var index = 37; index < body.Length; index += 4)
                body.AsSpan(index, 4).Reverse();
        }
        return [.. header, .. body];
    }

    public static string Gltf(string? bufferUri, bool draco = false, bool externalImage = false, bool texture = false)
    {
        var uri = bufferUri is null ? "" : $""","uri":"{bufferUri}" """;
        var compression = draco ? ""","extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}""" : "";
        var extension = draco ? ""","extensionsUsed":["KHR_draco_mesh_compression"],"extensionsRequired":["KHR_draco_mesh_compression"]""" : "";
        const string pixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aB9sAAAAASUVORK5CYII=";
        var image = externalImage || texture
            ? $$""","images":[{"uri":"{{(externalImage ? "absent.png" : pixel)}}"}],"textures":[{"source":0}]"""
            : "";
        return $$"""
            {"asset":{"version":"2.0","generator":"fixture"},"scene":0,"scenes":[{"nodes":[0]}],
             "nodes":[{"mesh":0,"translation":[2,0,0]}],
             "meshes":[{"primitives":[{"attributes":{"POSITION":0}{{compression}}}]}],
             "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}],
             "bufferViews":[{"buffer":0,"byteLength":36,"target":34962}],
             "buffers":[{"byteLength":36{{uri}}}]{{extension}}{{image}}}
            """;
    }

    public static string DataUri => "data:application/octet-stream;base64," + Convert.ToBase64String(PositionBytes());

    public static byte[] Glb(int extraBytes = 0)
    {
        var json = Encoding.UTF8.GetBytes(Gltf(null, texture: true));
        var padded = (json.Length + 3) & ~3;
        var data = new byte[28 + padded + 36 + extraBytes];
        "glTF"u8.CopyTo(data);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8), data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), padded);
        "JSON"u8.CopyTo(data.AsSpan(16));
        data.AsSpan(20, padded).Fill((byte)' ');
        json.CopyTo(data, 20);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20 + padded), 36 + extraBytes);
        "BIN\0"u8.CopyTo(data.AsSpan(24 + padded));
        PositionBytes().CopyTo(data, 28 + padded);
        return data;
    }

    private static byte[] PositionBytes()
    {
        var data = new byte[Positions.Length * sizeof(float)];
        for (var index = 0; index < Positions.Length; index++)
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(index * sizeof(float)), Positions[index]);
        return data;
    }
}
