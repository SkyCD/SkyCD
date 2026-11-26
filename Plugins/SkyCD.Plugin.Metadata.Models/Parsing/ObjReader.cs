using SkyCD.Plugin.Metadata.Models.Exceptions;
using SkyCD.Plugin.Metadata.Models.Models;

namespace SkyCD.Plugin.Metadata.Models.Parsing;

internal static class ObjReader
{
    public static void Read(byte[] data, ModelScan scan)
    {
        using var reader = ModelText.Open(data);
        var libraries = new List<string>();
        long vertices = 0, faces = 0, triangles = 0, objects = 0, groups = 0, materials = 0;
        while (reader.ReadLine() is { } line)
        {
            scan.Cancellation.ThrowIfCancellationRequested();
            var words = ModelText.Words(ModelText.WithoutComment(line));
            if (words.Length == 0)
                continue;

            switch (words[0])
            {
                case "v":
                    scan.Vertex(ModelText.Position(words, 1));
                    vertices++;
                    break;
                case "o":
                    objects++;
                    break;
                case "g":
                    groups++;
                    break;
                case "usemtl":
                    materials++;
                    break;
                case "mtllib":
                    libraries.AddRange(words.Skip(1).Take(20 - libraries.Count));
                    break;
                case "f":
                    if (words.Length < 4)
                        throw new ModelReadException("InvalidFace");
                    var first = Resolve(words[1], vertices);
                    var previous = Resolve(words[2], vertices);
                    for (var index = 3; index < words.Length; index++)
                    {
                        var current = Resolve(words[index], vertices);
                        scan.Face(first, previous, current, vertices);
                        previous = current;
                        triangles++;
                    }
                    faces++;
                    break;
            }
        }

        if (vertices == 0 && !scan.Truncated)
            throw new ModelReadException("MissingVertices");
        scan.Count("VertexCount", vertices);
        scan.Count("FaceCount", faces);
        scan.Count("TriangleCount", triangles);
        scan.Count("ObjectCount", objects);
        scan.Count("GroupCount", groups);
        scan.Count("MaterialUseCount", materials);
        scan.List("MtlLibraries", libraries);
    }

    private static int Resolve(string word, long vertices)
    {
        var index = ModelText.Integer(word.Split('/', 2)[0]);
        return index < 0 ? (int)vertices + index : index - 1;
    }
}
