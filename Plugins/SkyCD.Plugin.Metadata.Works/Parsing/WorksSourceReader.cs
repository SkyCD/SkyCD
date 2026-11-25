using System;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Metadata.Works.Exceptions;
using SkyCD.Plugin.Metadata.Works.Models;

namespace SkyCD.Plugin.Metadata.Works.Parsing;

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

    internal const string Mn0Stream = "MN0";
    internal const string ContentsStream = "CONTENTS";
    private const string SummaryStream = "\u0005SummaryInformation";
    private const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";
    private const string EncryptionInfoStream = "EncryptionInfo";

    private static ReadOnlySpan<byte> ChnkInk => [0x43, 0x48, 0x4E, 0x4B, 0x49, 0x4E, 0x4B];
    private static ReadOnlySpan<byte> ChnkWks => [0x43, 0x48, 0x4E, 0x4B, 0x57, 0x4B, 0x53];

    public static async Task<WorksSource> ReadAllAsync(
        MediaItem item,
        IMediaSource source,
        WorksPluginOptions options,
        CancellationToken cancellationToken)
    {
        var buffer = await ReadBytesAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        return ParseBuffer(buffer, options);
    }

    /// <summary>Reads the whole bounded source, charging the host budget exactly once.</summary>
    internal static async Task<byte[]> ReadBytesAsync(
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

        return buffer;
    }

    /// <summary>
    /// Recognizes a Works source from its bytes and reads the summary streams. Shared by metadata and
    /// preview so both capabilities apply the same structural and encryption rules.
    /// </summary>
    internal static WorksSource ParseBuffer(byte[] buffer, WorksPluginOptions options) =>
        CompoundFile.HasSignature(buffer)
            ? ReadCompound(buffer, options)
            : ReadFlat(buffer);

    /// <summary>
    /// States the Work generation carried by an already opened compound file, or raises the same decode and
    /// authentication exceptions the metadata capability raises.
    /// </summary>
    internal static string DetectCompoundFormat(CompoundFile compound)
    {
        if (compound.TryReadStreamPrefix(Mn0Stream, 16, out var mn0, out var mn0Length))
        {
            if (mn0Length < MinimumFlatBytes || mn0.Length < 2 || mn0[1] != 0xFE || mn0[0] > 7)
            {
                throw new WorksDecodeException("the MN0 stream does not begin with a Works header");
            }

            return ResolveCompoundVersion(mn0[0]);
        }

        if (compound.TryReadStreamPrefix(ContentsStream, 16, out var contents, out var contentsLength))
        {
            if (contentsLength < 7)
            {
                throw new WorksDecodeException("the CONTENTS stream is truncated");
            }

            var magic = contents.AsSpan(0, 7);
            if (magic.SequenceEqual(ChnkInk))
            {
                return "2000";
            }

            if (magic.SequenceEqual(ChnkWks))
            {
                return "7/8";
            }

            throw new WorksDecodeException("the CONTENTS stream does not begin with a Works chunk header");
        }

        // An encrypted Office document is an OLE wrapper with an EncryptionInfo stream and no readable body.
        if (compound.TryReadStreamPrefix(EncryptionInfoStream, 1, out _, out _))
        {
            throw new WorksAuthenticationRequiredException(
                "the compound document is wrapped in an encryption container");
        }

        throw new WorksDecodeException("the compound document does not contain a Works document stream");
    }

    private static WorksSource ReadCompound(byte[] data, WorksPluginOptions options)
    {
        var compound = CompoundFile.Open(data);
        var formatVersion = DetectCompoundFormat(compound);

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
