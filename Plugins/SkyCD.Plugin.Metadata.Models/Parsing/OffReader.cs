using SkyCD.Plugin.Metadata.Models.Exceptions;
using SkyCD.Plugin.Metadata.Models.Models;

namespace SkyCD.Plugin.Metadata.Models.Parsing;

internal static class OffReader
{
    public static void Read(byte[] data, ModelScan scan)
    {
        using var reader = ModelText.Open(data);
        var comments = new List<string>();

        string[] Next()
        {
            while (reader.ReadLine() is { } line)
            {
                scan.Cancellation.ThrowIfCancellationRequested();
                var comment = line.IndexOf('#');
                if (comment >= 0 && comments.Count < 20)
                    comments.Add(line[(comment + 1)..].Trim());

                var words = ModelText.Words(ModelText.WithoutComment(line));
                if (words.Length > 0)
                    return words;
            }
            return [];
        }

        var header = Next();
        if (header.Length == 0 || header[0] != "OFF")
            throw new ModelReadException("InvalidHeader");
        var counts = header.Length == 1 ? Next() : header[1..];
        if (counts.Length < 3)
        {
            if (scan.Truncated)
                return;
            throw new ModelReadException("InvalidHeader");
        }
        var vertices = ModelText.Count(counts[0]);
        var faces = ModelText.Count(counts[1]);
        _ = ModelText.Count(counts[2]);
        scan.Text("Encoding", "ascii");
        scan.Count("VertexCount", vertices);
        scan.Count("FaceCount", faces);

        var verticesIncomplete = false;
        for (var index = 0; index < vertices; index++)
        {
            var words = Next();
            if (words.Length == 0 && scan.Truncated)
            {
                verticesIncomplete = true;
                break;
            }
            scan.Vertex(ModelText.Position(words, 0));
        }

        long triangles = 0;
        for (var index = 0; index < faces && !verticesIncomplete; index++)
        {
            var words = Next();
            if (words.Length == 0 && scan.Truncated)
                break;
            var count = words.Length == 0 ? 0 : ModelText.Count(words[0]);
            if (count < 3 || count >= words.Length)
                throw new ModelReadException("InvalidFace");
            var first = ModelText.Integer(words[1]);
            var previous = ModelText.Integer(words[2]);
            for (var corner = 3; corner <= count; corner++)
            {
                var current = ModelText.Integer(words[corner]);
                scan.Face(first, previous, current, vertices);
                previous = current;
                triangles++;
            }
        }
        scan.Count("TriangleCount", triangles);
        scan.List("Comment", comments);
    }
}
