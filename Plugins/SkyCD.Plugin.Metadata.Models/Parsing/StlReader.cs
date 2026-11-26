using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using SkyCD.Plugin.Metadata.Models.Exceptions;
using SkyCD.Plugin.Metadata.Models.Models;

namespace SkyCD.Plugin.Metadata.Models.Parsing;

internal static class StlReader
{
    public static void Read(byte[] data, long length, ModelScan scan)
    {
        var count = data.Length >= 84 ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(80)) : 0;
        var ascii = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 80)).TrimStart()
            .StartsWith("solid", StringComparison.OrdinalIgnoreCase);
        var binary = data.Length >= 84 && (84L + count * 50L == length ||
            !ascii || data.AsSpan(0, 84).Contains((byte)0));

        scan.Flag("IsBinary", binary);
        if (binary)
        {
            if (84L + count * 50L > length)
                throw new ModelReadException("Truncated");
            scan.Text("HeaderText", Encoding.ASCII.GetString(data, 0, 80).Trim('\0', ' '));
            scan.Count("TriangleCount", count);
            scan.Count("VertexCount", count * 3L);
            var available = Math.Min(count, (data.Length - 84) / 50L);
            for (var index = 0; index < available; index++)
            {
                scan.Cancellation.ThrowIfCancellationRequested();
                var offset = 84 + index * 50 + 12;
                Add(scan, Position(data.AsSpan(offset)), Position(data.AsSpan(offset + 12)),
                    Position(data.AsSpan(offset + 24)));
            }
            return;
        }

        using var reader = ModelText.Open(ModelReader.CompleteLines(data, scan));
        var header = reader.ReadLine()?.Trim() ?? "";
        var headerWords = ModelText.Words(header);
        if (headerWords.Length == 0 || headerWords[0] != "solid")
            throw new ModelReadException("InvalidHeader");
        scan.Text("HeaderText", header[5..].Trim());

        long triangles = 0;
        var vertices = new Vector3[3];
        var corner = 0;
        var ended = false;
        var state = 0;
        while (reader.ReadLine() is { } line)
        {
            scan.Cancellation.ThrowIfCancellationRequested();
            var words = ModelText.Words(line);
            if (words.Length == 0)
                continue;
            switch (state, words[0])
            {
                case (0, "facet") when words.Length == 5 && words[1] == "normal":
                    _ = ModelText.Position(words, 2);
                    state = 1;
                    break;
                case (1, "outer") when words.Length == 2 && words[1] == "loop":
                    corner = 0;
                    state = 2;
                    break;
                case (2, "vertex") when corner < 3:
                    vertices[corner++] = ModelText.Position(words, 1);
                    break;
                case (2, "endloop") when corner == 3:
                    state = 3;
                    break;
                case (3, "endfacet"):
                    Add(scan, vertices[0], vertices[1], vertices[2]);
                    triangles++;
                    state = 0;
                    break;
                case (0, "endsolid"):
                    ended = true;
                    break;
                default:
                    throw new ModelReadException("InvalidFacet");
            }
            if (ended)
                break;
        }
        if (!ended && !scan.Truncated)
            throw new ModelReadException("Truncated");
        scan.Count("TriangleCount", triangles);
        scan.Count("VertexCount", triangles * 3);
    }

    private static Vector3 Position(ReadOnlySpan<byte> data) => new(
        BinaryPrimitives.ReadSingleLittleEndian(data),
        BinaryPrimitives.ReadSingleLittleEndian(data[4..]),
        BinaryPrimitives.ReadSingleLittleEndian(data[8..]));

    private static void Add(ModelScan scan, Vector3 a, Vector3 b, Vector3 c)
    {
        scan.Include(a);
        scan.Include(b);
        scan.Include(c);
        scan.Triangle(a, b, c);
    }
}
