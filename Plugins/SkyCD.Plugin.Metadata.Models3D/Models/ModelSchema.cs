using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Models3D.Models;

internal static class ModelSchema
{
    private static readonly string[] Extensions = [".glb", ".gltf", ".obj", ".stl", ".ply", ".off"];
    private static readonly string[] ContentTypes = ["model/gltf-binary", "model/gltf+json", "model/stl"];

    public static MediaDescriptor Metadata { get; } = new(Extensions, ContentTypes,
        new("skycd.models.metadata", 1,
        [
            .. Fields(MediaMetadataType.Text, "Format Version Generator ExtensionsUsed BoundingBoxMin BoundingBoxMax MtlLibraries HeaderText Encoding Comment FailureCode"),
            .. Fields(MediaMetadataType.Integer, "NodeCount MeshCount PrimitiveCount MaterialCount TextureCount ImageCount AnimationCount SkinCount VertexCount TriangleCount ObjectCount GroupCount FaceCount MaterialUseCount ElementCount"),
            .. Fields(MediaMetadataType.Boolean, "Valid ScanIncomplete HasEmbeddedBuffers IsBinary")
        ]), 10);

    public static MediaDescriptor Preview { get; } = new(Extensions, ContentTypes,
        new("skycd.models.preview", 1, []), 10, IndexTimePreview: true);

    private static IEnumerable<MediaMetadataField> Fields(MediaMetadataType type, string names) =>
        names.Split(' ').Select(name => new MediaMetadataField(name, type));
}
