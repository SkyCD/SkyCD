using System.Numerics;

namespace SkyCD.Plugin.Metadata.Models.Models;

internal readonly record struct ModelTriangle(Vector3 A, Vector3 B, Vector3 C, Vector4? Color);
