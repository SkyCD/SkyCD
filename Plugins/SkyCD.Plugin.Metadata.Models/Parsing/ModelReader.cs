using System.Text.Json;
using SkyCD.Plugin.Metadata.Models.Exceptions;
using SkyCD.Plugin.Metadata.Models.Models;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Models.Parsing;

internal static class ModelReader
{
    public static async Task<(IReadOnlyList<MediaMetadataValue> Values, List<ModelTriangle> Triangles)> ReadAsync(
        MediaItem item, IMediaSource source, CancellationToken cancellation)
    {
        var format = item.Extension.ToLowerInvariant() switch
        {
            ".gltf" => "glTF",
            ".glb" => "GLB",
            ".obj" => "OBJ",
            ".stl" => "STL",
            ".ply" => "PLY",
            ".off" => "OFF",
            _ => item.ContentType?.ToLowerInvariant() switch
            {
                "model/gltf+json" => "glTF",
                "model/gltf-binary" => "GLB",
                "model/stl" => "STL",
                _ => ""
            }
        };

        var length = source.Length >= 0 ? source.Length : item.Length;
        var limit = format switch
        {
            "GLB" when length > 16 * 1024 * 1024 => 4 * 1024 * 1024,
            "GLB" or "glTF" => 16 * 1024 * 1024,
            _ => 8 * 1024 * 1024
        };
        var data = new byte[(int)Math.Clamp(length, 0, Math.Min(limit, source.RemainingReadBytes))];
        var scan = new ModelScan(format, length > data.Length, cancellation);
        try
        {
            for (var offset = 0; offset < data.Length;)
            {
                cancellation.ThrowIfCancellationRequested();
                var chunk = await source.ReadAsync(offset, Math.Min(256 * 1024, data.Length - offset), cancellation)
                    .ConfigureAwait(false);
                if (chunk.IsEmpty)
                    throw new ModelReadException("Truncated");
                chunk.Span.CopyTo(data.AsSpan(offset));
                offset += chunk.Length;
            }

            if (data.Length == 0 && scan.Truncated)
                return (scan.Finish(), []);

            switch (format)
            {
                case "GLB": GlbReader.Read(data, length, scan); break;
                case "glTF": GltfReader.Read(data, data, scan); break;
                case "OBJ": ObjReader.Read(CompleteLines(data, scan), scan); break;
                case "STL": StlReader.Read(data, length, scan); break;
                case "PLY": PlyReader.Read(data, scan); break;
                case "OFF": OffReader.Read(CompleteLines(data, scan), scan); break;
                default: throw new ModelReadException("UnsupportedFormat");
            }

            return (scan.Finish(), scan.Triangles);
        }
        catch (ModelReadException error)
        {
            return (Invalid(format, error.Code), []);
        }
        catch (JsonException)
        {
            return scan.Truncated ? (scan.Finish(), scan.Triangles) : (Invalid(format, "InvalidJson"), []);
        }
        catch (SharpGLTF.Validation.DataException)
        {
            return (Invalid(format, "InvalidGeometry"), []);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or FormatException or OverflowException or IndexOutOfRangeException or EndOfStreamException)
        {
            return (Invalid(format, "InvalidStructure"), []);
        }
    }

    public static byte[] CompleteLines(byte[] data, ModelScan scan) =>
        scan.Truncated ? data[..(Array.LastIndexOf(data, (byte)'\n') + 1)] : data;

    private static MediaMetadataValue[] Invalid(string format, string code) =>
    [
        new("Format", MediaMetadataType.Text, Text: format),
        new("Valid", MediaMetadataType.Boolean, Boolean: false),
        new("FailureCode", MediaMetadataType.Text, Text: code)
    ];
}
