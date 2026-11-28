using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using SkyCD.Plugin.Metadata.Models3D.Exceptions;
using SkyCD.Plugin.Metadata.Models3D.Models;

namespace SkyCD.Plugin.Metadata.Models3D.Parsing;

internal static class PlyReader
{
    private sealed record Property(string Name, string Type, string? CountType = null);
    private sealed record Element(string Name, int Count, List<Property> Properties);

    public static void Read(byte[] data, ModelScan scan)
    {
        var offset = 0;
        string HeaderLine()
        {
            var end = Array.IndexOf(data, (byte)'\n', offset, Math.Min(data.Length - offset, 65536));
            if (end < 0 || end > 65536)
                throw new ModelReadException("InvalidHeader");
            var line = Encoding.ASCII.GetString(data, offset, end - offset).TrimEnd('\r');
            offset = end + 1;
            return line;
        }

        if (HeaderLine() != "ply")
            throw new ModelReadException("InvalidHeader");
        var format = ModelText.Words(HeaderLine());
        if (format.Length != 3 || format[0] != "format" || format[2] != "1.0" ||
            format[1] is not ("ascii" or "binary_little_endian" or "binary_big_endian"))
            throw new ModelReadException("InvalidEncoding");

        var elements = new List<Element>();
        var comments = new List<string>();
        while (HeaderLine() is var line && line != "end_header")
        {
            scan.Cancellation.ThrowIfCancellationRequested();
            var words = ModelText.Words(line);
            if (words.Length == 0)
                continue;
            switch (words[0])
            {
                case "comment":
                    if (comments.Count < 20)
                        comments.Add(line[7..].Trim());
                    break;
                case "obj_info":
                    break;
                case "element" when words.Length == 3 && elements.All(element => element.Name != words[1]):
                    elements.Add(new(words[1], ModelText.Count(words[2]), []));
                    break;
                case "property" when elements.Count > 0:
                    var property = words.Length switch
                    {
                        3 => new Property(words[2], words[1]),
                        5 when words[1] == "list" => new Property(words[4], words[3], words[2]),
                        _ => throw new ModelReadException("InvalidProperty")
                    };
                    _ = SizeOf(property.Type);
                    if (property.CountType is { } countType)
                    {
                        _ = SizeOf(countType);
                        if (!IsInteger(countType))
                            throw new ModelReadException("InvalidListType");
                    }
                    elements[^1].Properties.Add(property);
                    break;
                default:
                    throw new ModelReadException("InvalidHeader");
            }
        }

        var vertex = elements.Find(element => element.Name == "vertex");
        var face = elements.Find(element => element.Name == "face");
        if (vertex is null || new[] { "x", "y", "z" }.Any(axis =>
            !vertex.Properties.Any(property => property.Name == axis && property.CountType is null)))
            throw new ModelReadException("MissingVertices");

        scan.Text("Encoding", format[1]);
        scan.Count("ElementCount", elements.Count);
        scan.Count("VertexCount", vertex.Count);
        if (face is not null)
            scan.Count("FaceCount", face.Count);
        scan.List("Comment", comments);

        var ascii = format[1] == "ascii";
        var little = format[1] == "binary_little_endian";
        var endOfData = ascii && scan.Truncated ? Array.LastIndexOf(data, (byte)'\n') + 1 : data.Length;
        var faceIndices = new List<(int First, int Previous, int Current)>();
        long triangles = 0;
        try
        {
            foreach (var element in elements)
            {
                if (element.Count > 0 && element.Properties.Count == 0)
                    throw new ModelReadException("MissingProperties");

                for (var row = 0; row < element.Count; row++)
                {
                    scan.Cancellation.ThrowIfCancellationRequested();
                    var position = Vector3.Zero;

                    var rowTriangles = 0L;
                    foreach (var property in element.Properties)
                    {
                        if (property.CountType is null)
                        {
                            var value = (float)ReadNumber(property.Type);
                            if (element == vertex)
                            {
                                switch (property.Name)
                                {
                                    case "x": position.X = value; break;
                                    case "y": position.Y = value; break;
                                    case "z": position.Z = value; break;
                                }
                            }
                            continue;
                        }

                        var count = ReadNumber(property.CountType);
                        if (count < 0 || count > uint.MaxValue)
                            throw new ModelReadException("InvalidListCount");
                        var isFace = element == face && property.Name is "vertex_indices" or "vertex_index";
                        if (isFace && (!IsInteger(property.Type) || count < 3))
                            throw new ModelReadException("InvalidFace");
                        var first = 0;
                        var previous = 0;
                        for (var item = 0L; item < count; item++)
                        {
                            if (item % 4096 == 0)
                                scan.Cancellation.ThrowIfCancellationRequested();
                            var value = ReadNumber(property.Type);
                            if (!isFace)
                                continue;
                            if (value < 0 || value >= vertex.Count)
                                throw new ModelReadException("InvalidIndex");
                            var current = (int)value;
                            if (item == 0)
                                first = current;
                            else if (item >= 2)
                            {
                                if (faceIndices.Count < ModelScan.TriangleLimit)
                                    faceIndices.Add((first, previous, current));
                                rowTriangles++;
                            }
                            previous = current;
                        }
                    }

                    if (element == vertex)
                        scan.Vertex(position);
                    triangles += rowTriangles;
                }
            }
        }
        catch (EndOfStreamException)
        {
            if (!scan.Truncated)
                throw new ModelReadException("Truncated");
        }

        if (triangles > ModelScan.TriangleLimit)
            scan.Incomplete = true;
        foreach (var (first, previous, current) in faceIndices.Take((int)Math.Min(triangles, faceIndices.Count)))
            scan.Face(first, previous, current, vertex.Count);
        if (face is not null)
            scan.Count("TriangleCount", triangles);

        double ReadNumber(string type)
        {
            if (ascii)
            {
                while (offset < endOfData && char.IsWhiteSpace((char)data[offset]))
                    offset++;
                var start = offset;
                while (offset < endOfData && !char.IsWhiteSpace((char)data[offset]))
                    offset++;
                if (start == offset)
                    throw new EndOfStreamException();
                var text = data.AsSpan(start, offset - start);
                if (!double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                    (IsInteger(type) && (!double.IsFinite(value) || value != Math.Truncate(value))))
                    throw new ModelReadException("InvalidNumber");
                return value;
            }

            var size = SizeOf(type);
            if (size > endOfData - offset)
                throw new EndOfStreamException();
            var bytes = data.AsSpan(offset, size);
            offset += size;
            return type switch
            {
                "char" or "int8" => (sbyte)bytes[0],
                "uchar" or "uint8" => bytes[0],
                "short" or "int16" => little ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes),
                "ushort" or "uint16" => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
                "int" or "int32" => little ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes),
                "uint" or "uint32" => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
                "float" or "float32" => little ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : BinaryPrimitives.ReadSingleBigEndian(bytes),
                _ => little ? BinaryPrimitives.ReadDoubleLittleEndian(bytes) : BinaryPrimitives.ReadDoubleBigEndian(bytes)
            };
        }
    }

    private static int SizeOf(string type) => type switch
    {
        "char" or "int8" or "uchar" or "uint8" => 1,
        "short" or "int16" or "ushort" or "uint16" => 2,
        "int" or "int32" or "uint" or "uint32" or "float" or "float32" => 4,
        "double" or "float64" => 8,
        _ => throw new ModelReadException("InvalidPropertyType")
    };

    private static bool IsInteger(string type) => type is not ("float" or "float32" or "double" or "float64");
}
