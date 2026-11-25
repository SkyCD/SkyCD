using System;
using System.IO;
using System.Reflection;

namespace SkyCD.Plugin.Runtime.Factories;

/// <summary>
/// Reads the PNG icon a plugin ships inside its own assembly. Every plugin library embeds its icon at build
/// time under <see cref="EmbeddedIconResourceName"/>; the host never derives an icon from the assembly name
/// or from the capabilities the plugin happens to implement.
/// </summary>
internal static class PluginIconResolver
{
    /// <summary>Logical name of the PNG icon resource embedded in every plugin assembly.</summary>
    internal const string EmbeddedIconResourceName = "SkyCD.Plugin.Icon.png";

    /// <summary>Upper bound for icon payloads read from untrusted plugin assemblies.</summary>
    internal const int MaxIconBytes = 256 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Returns the embedded PNG icon of <paramref name="assembly"/>, or <see langword="null"/> when the
    /// assembly ships no icon or the resource is not a bounded PNG payload.
    /// </summary>
    public static byte[]? Resolve(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(EmbeddedIconResourceName);
        return stream is null ? null : Read(stream);
    }

    internal static byte[]? Read(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxIconBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        var icon = buffer.ToArray();
        return IsPng(icon) ? icon : null;
    }

    private static bool IsPng(byte[] payload) =>
        payload.Length > PngSignature.Length && payload.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature);
}
