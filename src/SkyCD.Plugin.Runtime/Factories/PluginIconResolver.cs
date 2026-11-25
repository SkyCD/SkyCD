using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SkyCD.Plugin.Runtime.Factories;

/// <summary>
/// Reads the ICO icon a plugin ships inside its own assembly. Every plugin library embeds its icon at build
/// time under <see cref="EmbeddedIconResourceName"/>; the host never derives an icon from the assembly name
/// or from the capabilities the plugin happens to implement.
/// </summary>
internal static class PluginIconResolver
{
    /// <summary>Logical name of the ICO icon resource embedded in every plugin assembly.</summary>
    internal const string EmbeddedIconResourceName = "SkyCD.Plugin.Icon.ico";

    /// <summary>Upper bound for icon payloads read from untrusted plugin assemblies.</summary>
    internal const int MaxIconBytes = 256 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Returns the largest PNG frame from the embedded ICO, or <see langword="null"/> for invalid icons.
    /// </summary>
    public static byte[]? Resolve(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(EmbeddedIconResourceName);
        return stream is null ? null : Read(stream);
    }

    /// <summary>
    /// Returns every valid square PNG frame from the embedded ICO, or an empty list for invalid icons.
    /// </summary>
    internal static IReadOnlyList<PluginIconFrame> ReadFrames(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(EmbeddedIconResourceName);
        return stream is null ? [] : ReadFrames(stream);
    }

    internal static byte[]? Read(Stream stream)
    {
        return ReadFrames(stream).MaxBy(static frame => frame.Size)?.Png;
    }

    internal static IReadOnlyList<PluginIconFrame> ReadFrames(Stream stream)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxIconBytes)
            {
                return [];
            }

            buffer.Write(chunk, 0, read);
        }

        var icon = buffer.ToArray().AsSpan();
        if (icon.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(icon) != 0 ||
            BinaryPrimitives.ReadUInt16LittleEndian(icon[2..]) != 1)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(icon[4..]);
        var directoryEnd = 6 + (16 * count);
        if (count == 0 || directoryEnd > icon.Length)
        {
            return [];
        }

        var frames = new List<PluginIconFrame>();
        for (var i = 0; i < count; i++)
        {
            var entry = icon.Slice(6 + (16 * i), 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            var length = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (width != height || offset < directoryEnd ||
                offset > icon.Length || length > icon.Length - offset || length <= PngSignature.Length)
            {
                continue;
            }

            var frame = icon.Slice((int)offset, (int)length);
            if (frame[..PngSignature.Length].SequenceEqual(PngSignature))
            {
                frames.Add(new PluginIconFrame(width, frame.ToArray()));
            }
        }

        return frames;
    }
}
