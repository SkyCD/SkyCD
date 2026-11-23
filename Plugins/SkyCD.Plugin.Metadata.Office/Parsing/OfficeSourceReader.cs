using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a bounded source into memory and resolves the metadata it carries.
/// </summary>
/// <remarks>
/// Reading in one pass keeps the host's budget accounting simple: the charge equals the source size, once.
/// The plugin only ever holds bytes and never a path, so a document that references another file has no way
/// to make it open that file. A damaged optional metadata part is skipped, while a source that is neither an
/// OOXML package nor an Office compound document raises <see cref="OfficeDecodeException"/>.
/// </remarks>
internal static class OfficeSourceReader
{
    private const int ChunkBytes = 256 * 1024;

    public const string SummaryStream = "\u0005SummaryInformation";
    public const string DocumentSummaryStream = "\u0005DocumentSummaryInformation";
    public const string EncryptionInfoStream = "EncryptionInfo";
    public const string EncryptedPackageStream = "EncryptedPackage";

    private static readonly IReadOnlyDictionary<uint, object> NoValues = new Dictionary<uint, object>();

    public static async Task<OfficeMetadata> ReadAllAsync(
        MediaItem item,
        IMediaSource source,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var data = await ReadSourceAsync(item, source, options, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (OfficePackageReader.HasSignature(data))
        {
            return OfficePackageReader.Read(data, options, cancellationToken);
        }

        if (CompoundFile.HasSignature(data))
        {
            return ReadCompound(data, options);
        }

        throw new OfficeDecodeException("the source is neither an OOXML package nor an OLE2 compound file");
    }

    private static OfficeMetadata ReadCompound(byte[] data, OfficePluginOptions options)
    {
        var compound = CompoundFile.Open(data);

        // Office 2007 and later protect an OOXML package by wrapping it, unchanged, in a compound file.
        if (IsEncryptedWrapper(compound))
        {
            return OfficeMetadata.Encrypted;
        }

        var documentType = DetectCompoundDocumentType(compound)
            ?? throw new OfficeDecodeException(
                "the compound document does not contain a Microsoft Office document stream");

        return OfficeMetadata.FromCompound(
            documentType,
            ReadPropertyStream(compound, SummaryStream, options),
            ReadPropertyStream(compound, DocumentSummaryStream, options));
    }

    /// <summary>True when the compound file wraps an encrypted OOXML package rather than a legacy document.</summary>
    public static bool IsEncryptedWrapper(CompoundFile compound) =>
        compound.ContainsStream(EncryptionInfoStream) && compound.ContainsStream(EncryptedPackageStream);

    private static string? DetectCompoundDocumentType(CompoundFile compound)
    {
        if (compound.ContainsStream("WordDocument"))
        {
            return "word";
        }

        if (compound.ContainsStream("Workbook") || compound.ContainsStream("Book"))
        {
            return "excel";
        }

        return compound.ContainsStream("PowerPoint Document") ? "powerpoint" : null;
    }

    private static IReadOnlyDictionary<uint, object> ReadPropertyStream(
        CompoundFile compound,
        string name,
        OfficePluginOptions options)
    {
        try
        {
            return compound.TryReadStream(name, options.MaxMetadataPartBytes, out var stream)
                ? OfficePropertySet.Parse(stream)
                : NoValues;
        }
        catch (OfficeDecodeException)
        {
            // The summary is optional: a damaged chain costs metadata, not the whole extraction.
            return NoValues;
        }
    }

    /// <summary>Reads the whole source into memory, charging the host budget exactly once.</summary>
    public static async Task<byte[]> ReadSourceAsync(
        MediaItem item,
        IMediaSource source,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        var length = source.Length > 0 ? source.Length : item.Length;
        if (length <= 0)
        {
            throw new OfficeDecodeException("the source is empty");
        }

        if (length > options.MaxSourceBytes || length > int.MaxValue)
        {
            throw new OfficeSourceTooLargeException(length, options.MaxSourceBytes);
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
                throw new OfficeDecodeException($"the source returned {chunk.Length} of {count} requested bytes");
            }

            chunk.Span.CopyTo(buffer.AsSpan((int)offset, count));
            offset += count;
        }

        return buffer;
    }
}
