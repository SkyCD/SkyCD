using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Works.Exceptions;

namespace SkyCD.Plugin.Works;

/// <summary>The parsed document bytes, storage kind and optional summary streams.</summary>
internal sealed record WorksSource(byte[] Data, string Storage, string FormatVersion, byte[]? Summary,
    byte[]? DocumentSummary);

/// <summary>
/// Reads a bounded source into memory and locates the Microsoft Works document structure.
/// </summary>
/// <remarks>
/// Reading in one pass keeps the host's budget accounting simple: the charge equals the source size, once.
/// The plugin only ever holds bytes and never a path, so a document that references another file has no way
/// to make it open that file. A damaged optional summary stream is skipped, while a damaged document
/// structure raises <see cref="WorksDecodeException"/>.
/// </remarks>
internal static class WorksSourceReader
{
    private const int ChunkBytes = 256 * 1024;
    private const int MinimumFlatBytes = 0x100;

    private const string Mn0Stream = "MN0";
    private const string ContentsStream = "CONTENTS";
    private const string SummaryStream = "\u0005SummaryInformation";
    private const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";

    private static ReadOnlySpan<byte> ChnkInk => [0x43, 0x48, 0x4E, 0x4B, 0x49, 0x4E, 0x4B];
    private static ReadOnlySpan<byte> ChnkWks => [0x43, 0x48, 0x4E, 0x4B, 0x57, 0x4B, 0x53];

    public static async Task<WorksSource> ReadAllAsync(
        MediaItem item,
        IMediaSource source,
        WorksPluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new WorksDecodeException("the source is empty");
        }

        if (length > options.MaxSourceBytes || length > int.MaxValue)
        {
            throw new WorksSourceTooLargeException(length, options.MaxSourceBytes);
        }

        var buffer = new byte[length];
        var offset = 0L;
        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = (int)Math.Min(ChunkBytes, length - offset);
            var chunk = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (chunk.Length != count)
            {
                throw new WorksDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        return CompoundFile.HasSignature(buffer)
            ? ReadCompound(buffer, options)
            : ReadFlat(buffer);
    }

    private static WorksSource ReadCompound(byte[] data, WorksPluginOptions options)
    {
        var compound = CompoundFile.Open(data);
        string formatVersion;

        if (compound.TryReadStreamPrefix(Mn0Stream, 16, out var mn0, out var mn0Length))
        {
            if (mn0Length < MinimumFlatBytes || mn0.Length < 2 || mn0[1] != 0xFE || mn0[0] > 7)
            {
                throw new WorksDecodeException("the MN0 stream does not begin with a Works header");
            }

            formatVersion = ResolveCompoundVersion(mn0[0]);
        }
        else if (compound.TryReadStreamPrefix(ContentsStream, 16, out var contents, out var contentsLength))
        {
            if (contentsLength < 7)
            {
                throw new WorksDecodeException("the CONTENTS stream is truncated");
            }

            var magic = contents.AsSpan(0, 7);
            if (magic.SequenceEqual(ChnkInk))
            {
                formatVersion = "2000";
            }
            else if (magic.SequenceEqual(ChnkWks))
            {
                formatVersion = "7/8";
            }
            else
            {
                throw new WorksDecodeException("the CONTENTS stream does not begin with a Works chunk header");
            }
        }
        else
        {
            throw new WorksDecodeException("the compound document does not contain a Works document stream");
        }

        return new WorksSource(data, "compound", formatVersion,
            TryReadPropertyStream(compound, SummaryStream, options),
            TryReadPropertyStream(compound, DocumentSummaryStream, options));
    }

    private static WorksSource ReadFlat(byte[] data)
    {
        if (data.Length < MinimumFlatBytes || !WorksDocument.HasFlatHeader(data))
        {
            throw new WorksDecodeException("the source is not a Microsoft Works document");
        }

        var formatVersion = data[0] < 4 ? "2" : "3";
        return new WorksSource(data, "flat", formatVersion, null, null);
    }

    private static byte[]? TryReadPropertyStream(CompoundFile compound, string name, WorksPluginOptions options)
    {
        try
        {
            return compound.TryReadStream(name, options.MaxPropertyStreamBytes, out var stream) ? stream : null;
        }
        catch (WorksDecodeException)
        {
            // The summary is optional: a damaged chain costs metadata, not the whole extraction.
            return null;
        }
    }

    private static string ResolveCompoundVersion(byte headerVersion) => headerVersion switch
    {
        >= 6 => "4",
        >= 4 => "3",
        _ => "2"
    };
}
