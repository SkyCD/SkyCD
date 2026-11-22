using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Export;
using SkyCD.Plugin.Metadata.Executable.Models;

namespace SkyCD.Plugin.Metadata.Executable.Parsing;

/// <summary>
/// Walks an executable's icon groups, decodes the largest image of the first usable group and encodes it.
/// </summary>
/// <remarks>
/// The largest image is chosen from the sizes the group declares, before any pixel is read, so the most
/// detailed icon is found without decoding every size. A group or image that is damaged only removes that
/// candidate: the next image and the next group are still tried, and an executable whose icons are all broken
/// simply produces no preview.
/// </remarks>
internal static class ExecutableIconExtractor
{
    /// <summary>An icon directory caps its declared size at 256 pixels, which is what a zero field means.</summary>
    public const int LargestDeclaredSize = 256;

    /// <summary>One icon candidate as the group describes it.</summary>
    public readonly record struct IconCandidate(int ImageIndex, int Width, int Height, int BitCount);

    /// <summary>The result of one extraction: the encoded PNG or the facts explaining that there is none.</summary>
    public sealed record IconExtraction(byte[]? Png, int ImageCount, int LargestWidth, int LargestHeight)
    {
        public static IconExtraction None(int imageCount = 0, int largestWidth = 0, int largestHeight = 0) =>
            new(null, imageCount, largestWidth, largestHeight);
    }

    /// <summary>
    /// Extracts the preview. Every condition that means "this file has no usable icon" is reported rather than
    /// thrown; cancellation and the host's source-budget errors still propagate.
    /// </summary>
    public static async Task<IconExtraction> ExtractAsync(
        ExecutableSource source,
        ExecutableResources resources,
        ExecutablePluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(options);

        var icons = new Dictionary<int, ExecutableResource>();
        foreach (var icon in resources.Icons)
        {
            icons.TryAdd(icon.Name, icon);
        }

        var examined = 0;
        var largestWidth = 0;
        var largestHeight = 0;
        var imageCount = 0;

        foreach (var group in resources.IconGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Length > options.MaxIconBytes)
            {
                continue;
            }

            byte[] directory;
            try
            {
                directory = await source.ReadAsync(group.DataOffset, group.Length, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ExecutableReadException)
            {
                continue;
            }

            var candidates = ParseGroup(directory);
            if (candidates is null || candidates.Count == 0)
            {
                continue;
            }

            imageCount = Math.Max(imageCount, candidates.Count);
            foreach (var candidate in candidates)
            {
                largestWidth = Math.Max(largestWidth, candidate.Width);
                largestHeight = Math.Max(largestHeight, candidate.Height);
            }

            // Largest declared image first; resource ids are the group's one-based index into the icon list.
            candidates.Sort(static (left, right) =>
            {
                var area = ((long)right.Width * right.Height).CompareTo((long)left.Width * left.Height);
                return area != 0 ? area : right.BitCount.CompareTo(left.BitCount);
            });

            foreach (var candidate in candidates)
            {
                if (examined >= options.MaxIndexedIconImages)
                {
                    break;
                }

                if (!icons.TryGetValue(candidate.ImageIndex, out var resource) ||
                    resource.Length > options.MaxIconBytes)
                {
                    continue;
                }

                examined++;
                byte[] data;
                try
                {
                    data = await source.ReadAsync(resource.DataOffset, resource.Length, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (ExecutableReadException)
                {
                    continue;
                }

                var png = TryDecode(data, options);
                if (png is not null)
                {
                    return new IconExtraction(png, imageCount, largestWidth, largestHeight);
                }
            }
        }

        return IconExtraction.None(imageCount, largestWidth, largestHeight);
    }

    /// <summary>Reads an icon group: a reserved word, the type, the count, then one 14-byte entry per image.</summary>
    internal static List<IconCandidate>? ParseGroup(ReadOnlySpan<byte> directory)
    {
        if (directory.Length < 6)
        {
            return null;
        }

        var count = BitConverter.ToUInt16(directory.Slice(4, 2));
        if (count is 0 or > 256 || directory.Length < 6 + (count * 14))
        {
            return null;
        }

        var candidates = new List<IconCandidate>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = directory.Slice(6 + (index * 14), 14);
            var width = entry[0] == 0 ? LargestDeclaredSize : entry[0];
            var height = entry[1] == 0 ? LargestDeclaredSize : entry[1];
            var bitCount = BitConverter.ToUInt16(entry.Slice(6, 2));
            var bytes = BitConverter.ToUInt32(entry.Slice(8, 4));
            if (bytes == 0)
            {
                continue;
            }

            candidates.Add(new IconCandidate(index + 1, width, height, bitCount));
        }

        return candidates;
    }

    private static byte[]? TryDecode(byte[] data, ExecutablePluginOptions options)
    {
        try
        {
            var icon = ExecutableIconDecoder.Decode(data, ExecutablePluginOptions.MaximumIconPixels);
            return ExecutableIconEncoder.TryEncode(icon, options.IconSize, options.MaxPngBytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A damaged image, an unsupported bitmap depth, a bomb or an oversized encode is simply no icon.
            return null;
        }
    }
}
